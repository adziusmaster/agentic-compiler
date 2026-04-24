#!/usr/bin/env python3
"""Evaluate the LoRA-fine-tuned qwen2.5-coder-1.5b on the 30-problem AGC bench.

For each problem:
  1. Call the local model with ONLY the objective + tests.ag (no spec).
  2. Parse the generated module.
  3. Run `agc check` on it.
  4. Record pass/fail, tokens, wall-time, attempts.

Output: bench/results/YYYY-MM-DD/agc-local.jsonl (harness-compatible format).
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import tempfile
import time
from pathlib import Path

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


def build_prompt(objective: str, tests_ag: str) -> str:
    return (f"Write an AGC module that satisfies this objective:\n\n{objective}\n\n"
            f"Your module MUST include these tests exactly:\n\n"
            f"```\n{tests_ag}\n```\n\nOutput ONLY the module source.")


def build_retry_prompt(objective: str, tests_ag: str,
                       previous_source: str, diagnostic: str) -> str:
    return (
        f"Your previous AGC module failed. Here is the diagnostic from agc check:\n\n"
        f"{diagnostic}\n\n"
        f"Previous module:\n```\n{previous_source}\n```\n\n"
        f"Rewrite the module from scratch to satisfy this objective:\n\n{objective}\n\n"
        f"Include these tests exactly:\n\n```\n{tests_ag}\n```\n\n"
        f"Output ONLY the corrected module source."
    )


def generate_with_lora(model: str, adapter: str, prompt: str,
                       max_tokens: int = 1024) -> tuple[str, int, int]:
    """Invoke mlx_lm.generate CLI. Returns (raw_text, tokens_in, tokens_out)."""
    cmd = [
        sys.executable, "-m", "mlx_lm", "generate",
        "--model", model,
        "--adapter-path", adapter,
        "--system-prompt", SYSTEM_PROMPT,
        "--prompt", prompt,
        "--max-tokens", str(max_tokens),
        "--temp", "0.0",
        "--extra-eos-token", "<|im_end|>",
    ]
    t0 = time.monotonic()
    proc = subprocess.run(cmd, capture_output=True, text=True, timeout=120)
    dt = time.monotonic() - t0
    out = proc.stdout
    # mlx_lm.generate prints the body between "==========" markers
    parts = out.split("==========")
    body = parts[1].strip() if len(parts) >= 3 else out
    # tokens
    tin = tout = 0
    m_in = re.search(r"Prompt: (\d+) tokens", out)
    m_out = re.search(r"Generation: (\d+) tokens", out)
    if m_in:  tin = int(m_in.group(1))
    if m_out: tout = int(m_out.group(1))
    return body, tin, tout


def extract_module(text: str) -> str | None:
    """Pull the (module ...) form out of raw generation, discarding noise.
    Truncates at <|im_end|> and ignores content inside string literals during
    paren-balancing."""
    # Cut at Qwen chat end token; the model often continues with README noise after
    end_marker = text.find("<|im_end|>")
    if end_marker != -1:
        text = text[:end_marker]
    start = text.find("(module")
    if start == -1:
        return None
    depth = 0
    in_string = False
    escape = False
    for i in range(start, len(text)):
        ch = text[i]
        if in_string:
            if escape:
                escape = False
            elif ch == "\\":
                escape = True
            elif ch == '"':
                in_string = False
            continue
        if ch == '"':
            in_string = True
        elif ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
            if depth == 0:
                return text[start:i+1]
    return None


def verify(source: str) -> tuple[bool, int, int, str]:
    with tempfile.NamedTemporaryFile("w", suffix=".ag", delete=False, dir="/tmp") as f:
        f.write(source)
        path = f.name
    cmd = ["dotnet", str(AGC_CLI_DLL), "check", path,
           "--allow-env", "--allow-file", "--allow-http", "--allow-db"]
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True, timeout=20)
        out = proc.stdout + "\n" + proc.stderr
        m = TESTS_OK_RE.search(out)
        if m and int(m.group(2)) > 0:
            return True, int(m.group(1)), int(m.group(2)), ""
        m = TESTS_ANY_RE.search(out)
        if m:
            return False, int(m.group(1)), int(m.group(2)), out[-400:]
        return False, 0, 0, out[-400:]
    except subprocess.TimeoutExpired:
        return False, 0, 0, "timeout"


def count_loc(source: str) -> int:
    return sum(1 for line in source.splitlines() if line.strip())


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default="mlx-community/Qwen2.5-Coder-1.5B-Instruct-4bit")
    ap.add_argument("--adapter", default=str(TRAIN_DIR / "lora_adapter"))
    ap.add_argument("--only", help="comma-sep problem prefixes, e.g. 01,11,21")
    ap.add_argument("--out", help="override output jsonl path")
    ap.add_argument("--max-attempts", type=int, default=5,
                    help="retry budget per problem (feed agc-check errors back)")
    args = ap.parse_args()

    date = time.strftime("%Y-%m-%d")
    out_path = Path(args.out) if args.out else BENCH_RESULTS / date / "agc-local.jsonl"
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

        t0 = time.monotonic()
        tin_sum = tout_sum = 0
        module = None
        ok = False
        pv = tv = 0
        err = ""
        last_err_cat = None
        attempt = 0

        for attempt in range(1, args.max_attempts + 1):
            prompt = (build_prompt(obj, tests_ag) if attempt == 1
                      else build_retry_prompt(obj, tests_ag, module or "", err))
            try:
                raw, tin, tout = generate_with_lora(args.model, args.adapter, prompt)
            except Exception as e:
                err = f"gen-error: {e}"
                last_err_cat = "gen-error"
                break
            tin_sum += tin
            tout_sum += tout
            new_module = extract_module(raw)
            if new_module is None:
                err = raw[:400]
                last_err_cat = "parse-error"
                module = module or ""  # keep last successful module for retry feedback
                continue
            module = new_module
            ok, pv, tv, err_tail = verify(module)
            if ok:
                last_err_cat = None
                break
            err = err_tail
            last_err_cat = "test-fail"

        wall = time.monotonic() - t0
        loc = count_loc(module) if module else 0
        helpers = len(re.findall(r"\(defun\b", module)) if module else 0
        rec = {"id": p.name, "track": "agc-local", "pass": ok,
               "attempts": attempt, "wall_time_s": wall,
               "tokens_in": tin_sum, "tokens_out": tout_sum, "source_loc": loc,
               "capabilities": [], "decomposition_depth": helpers,
               "error_category": last_err_cat,
               "error_detail": None if ok else err[:400]}
        records.append(rec)
        if ok: passed += 1
        status = "PASS" if ok else "FAIL"
        print(f"[{status}] {p.name:30} attempts={attempt} wall={wall:.1f}s "
              f"tok={tin_sum}/{tout_sum} loc={loc} tests={pv}/{tv}", flush=True)

    with out_path.open("w") as fh:
        for r in records:
            fh.write(json.dumps(r) + "\n")

    total_wall = time.monotonic() - t_start
    print(f"\n=== SUMMARY ===")
    print(f"pass: {passed}/{len(records)} ({100*passed/len(records):.0f}%)")
    mean_in = sum(r["tokens_in"] for r in records) / max(1, len(records))
    mean_out = sum(r["tokens_out"] for r in records) / max(1, len(records))
    print(f"mean tokens_in:  {mean_in:.0f}")
    print(f"mean tokens_out: {mean_out:.0f}")
    print(f"total wall: {total_wall:.0f}s")
    print(f"results: {out_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
