#!/usr/bin/env python3
"""Best-of-N evaluation: load model+adapter once, sample N candidates per
problem at temperature > 0, verify each via agc check, keep the best.

For small specialised models, sampling diversity + verifier scoring is
the highest-leverage technique short of changing the model.

Usage:
  python3 eval_bestof_n.py --n 8 --temp 0.7 \
    [--model mlx-community/Qwen2.5-Coder-3B-Instruct-4bit] \
    [--adapter train/lora_adapter] \
    [--only 01,02,03] [--out path/to.jsonl]
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import tempfile
import time
from pathlib import Path

from mlx_lm import load, generate
from mlx_lm.sample_utils import make_sampler

TRAIN_DIR = Path(__file__).resolve().parent
REPO_ROOT = TRAIN_DIR.parent
BENCH_PROBLEMS = REPO_ROOT / "bench" / "problems"
BENCH_RESULTS = REPO_ROOT / "bench" / "results"
AGC_CLI_DLL = REPO_ROOT / "Agentic.Cli" / "bin" / "Debug" / "net8.0" / "Agentic.Cli.dll"

SYSTEM_PROMPT = "Output only AGC (Agentic Compiler) S-expression source code, no prose."

TESTS_OK_RE = re.compile(r"\(ok \(tests-passed (\d+)/(\d+)\)\)")
TESTS_ANY_RE = re.compile(r"tests-passed (\d+)/(\d+)")


def read(path: Path) -> str:
    return path.read_text()


def extract_signature_hints(tests_ag: str) -> str:
    seen: dict[str, int] = {}
    pattern = re.compile(r"\((?:assert-eq|assert-near|eq\?|near\?)\s+\(([a-z_][a-z0-9_]*)\b([^)]*?)\)")
    for m in pattern.finditer(tests_ag):
        name, argstr = m.group(1), m.group(2)
        depth = 0; in_str = False; toks = 0; cur = ""
        for ch in argstr.strip():
            if in_str:
                if ch == '"': in_str = False
                cur += ch; continue
            if ch == '"': in_str = True; cur += ch
            elif ch == "(": depth += 1; cur += ch
            elif ch == ")": depth -= 1; cur += ch
            elif ch.isspace() and depth == 0:
                if cur: toks += 1; cur = ""
            else: cur += ch
        if cur: toks += 1
        if name not in seen: seen[name] = toks
    if not seen: return ""
    lines = [f"  - `{n}` takes {a} argument(s)" for n, a in seen.items()]
    return "Function signatures (from the test calls):\n" + "\n".join(lines)


def build_prompt(objective: str, tests_ag: str) -> str:
    sig = extract_signature_hints(tests_ag)
    return (f"Write an AGC module that satisfies this objective:\n\n{objective}\n\n"
            + (f"{sig}\n\n" if sig else "")
            + f"The module must include these (test ...) blocks:\n\n{tests_ag}\n\n"
            + "Output ONLY the module source.")


def chat_format(tokenizer, system: str, user: str) -> str:
    msgs = [{"role": "system", "content": system}, {"role": "user", "content": user}]
    return tokenizer.apply_chat_template(msgs, tokenize=False, add_generation_prompt=True)


def extract_module(text: str) -> str | None:
    end_marker = text.find("<|im_end|>")
    if end_marker != -1:
        text = text[:end_marker]
    start = text.find("(module")
    if start == -1: return None
    depth = 0; in_string = False; escape = False
    for i in range(start, len(text)):
        ch = text[i]
        if in_string:
            if escape: escape = False
            elif ch == "\\": escape = True
            elif ch == '"': in_string = False
            continue
        if ch == '"': in_string = True
        elif ch == "(": depth += 1
        elif ch == ")":
            depth -= 1
            if depth == 0: return text[start:i+1]
    return None


def verify(source: str) -> tuple[bool, int, int, str]:
    with tempfile.NamedTemporaryFile("w", suffix=".ag", delete=False, dir="/tmp") as f:
        f.write(source); path = f.name
    cmd = ["dotnet", str(AGC_CLI_DLL), "check", path,
           "--allow-env", "--allow-file", "--allow-http", "--allow-db"]
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True, timeout=20)
        out = proc.stdout + "\n" + proc.stderr
        m = TESTS_OK_RE.search(out)
        if m and int(m.group(2)) > 0:
            return True, int(m.group(1)), int(m.group(2)), ""
        m = TESTS_ANY_RE.search(out)
        if m: return False, int(m.group(1)), int(m.group(2)), out[-400:]
        return False, 0, 0, out[-400:]
    except subprocess.TimeoutExpired:
        return False, 0, 0, "timeout"
    finally:
        try: os.unlink(path)
        except Exception: pass


def count_loc(source: str) -> int:
    return sum(1 for line in source.splitlines() if line.strip())


def sample_candidates(model, tokenizer, prompt: str, n: int, temp: float,
                      max_tokens: int) -> list[str]:
    """Sample n candidates at the given temperature. Returns raw text outputs."""
    samplers = []
    for i in range(n):
        # Vary temperature slightly per slot for diversity, plus top-p
        samplers.append(make_sampler(temp=temp, top_p=0.95))
    candidates = []
    for sampler in samplers:
        text = generate(model, tokenizer, prompt=prompt,
                        max_tokens=max_tokens, sampler=sampler, verbose=False)
        candidates.append(text)
    return candidates


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default="mlx-community/Qwen2.5-Coder-3B-Instruct-4bit")
    ap.add_argument("--adapter", default=str(TRAIN_DIR / "lora_adapter"))
    ap.add_argument("--only", help="comma-sep problem prefixes")
    ap.add_argument("--out", help="override output jsonl path")
    ap.add_argument("--n", type=int, default=8, help="candidates per problem")
    ap.add_argument("--temp", type=float, default=0.7, help="sampling temperature")
    ap.add_argument("--max-tokens", type=int, default=600)
    args = ap.parse_args()

    print(f"Loading {args.model} + adapter {args.adapter}...", flush=True)
    model, tokenizer = load(args.model, adapter_path=args.adapter)
    print("Loaded.", flush=True)

    date = time.strftime("%Y-%m-%d")
    out_path = Path(args.out) if args.out else BENCH_RESULTS / date / "agc-local-bestof.jsonl"
    out_path.parent.mkdir(parents=True, exist_ok=True)

    problems = sorted(p for p in BENCH_PROBLEMS.iterdir() if p.is_dir())
    if args.only:
        keys = args.only.split(",")
        problems = [p for p in problems if any(p.name.startswith(k) for k in keys)]

    records = []
    passed = 0
    t_start = time.monotonic()

    for p in problems:
        obj = read(p / "objective.md").strip()
        tests_ag = read(p / "tests.ag").strip()
        user_prompt = build_prompt(obj, tests_ag)
        chat_prompt = chat_format(tokenizer, SYSTEM_PROMPT, user_prompt)

        t0 = time.monotonic()
        # Sample N candidates
        cands = sample_candidates(model, tokenizer, chat_prompt, args.n, args.temp, args.max_tokens)

        # Verify each, track best
        best = {"pass": False, "passed": 0, "total": 0, "module": None, "err": ""}
        all_results = []
        for c in cands:
            module = extract_module(c)
            if module is None:
                all_results.append((False, 0, 0, "parse-error", c[:200]))
                continue
            ok, pv, tv, err = verify(module)
            all_results.append((ok, pv, tv, "ok" if ok else "test-fail", err[:200]))
            score = (1 if ok else 0, pv / max(1, tv))
            best_score = (1 if best["pass"] else 0, best["passed"] / max(1, best["total"]))
            if score > best_score:
                best = {"pass": ok, "passed": pv, "total": tv, "module": module, "err": err}
            if ok:  # early stop on first perfect candidate
                break

        wall = time.monotonic() - t0
        loc = count_loc(best["module"]) if best["module"] else 0
        # Approximate tokens: prompt token count once + generation tokens × n
        prompt_toks = len(tokenizer.encode(chat_prompt))
        # Generation tokens are the candidate text minus chat-end markers; rough
        gen_toks = sum(len(tokenizer.encode(c)) for c in cands)
        rec = {
            "id": p.name, "track": "agc-local-bestof",
            "pass": best["pass"], "attempts": args.n,
            "wall_time_s": wall,
            "tokens_in": prompt_toks * args.n, "tokens_out": gen_toks,
            "source_loc": loc,
            "capabilities": [], "decomposition_depth": 0,
            "tests_passed": best["passed"], "tests_total": best["total"],
            "error_category": None if best["pass"] else "test-fail",
            "error_detail": None if best["pass"] else best["err"][:300],
        }
        records.append(rec)
        if best["pass"]: passed += 1
        status = "PASS" if best["pass"] else "FAIL"
        print(f"[{status}] {p.name:30} N={args.n} wall={wall:.1f}s "
              f"best={best['passed']}/{best['total']} loc={loc}", flush=True)

    with out_path.open("w") as fh:
        for r in records:
            fh.write(json.dumps(r) + "\n")

    total = time.monotonic() - t_start
    print(f"\n=== SUMMARY ===")
    print(f"pass: {passed}/{len(records)} ({100*passed/len(records):.0f}%)")
    print(f"total wall: {total:.0f}s")
    print(f"results: {out_path}")


if __name__ == "__main__":
    sys.exit(main() or 0)
