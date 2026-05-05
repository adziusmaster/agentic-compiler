#!/usr/bin/env python3
"""Cascading multi-adapter eval.

Given a list of LoRA adapters in priority order:
  1. Try each adapter greedy in turn — early-exit on pass.
  2. If all greedy fail, sample N times on each adapter at temperature.

This banks every adapter's unique wins while keeping token cost low for
problems that pass on the first cheap greedy attempt.

Usage:
  python3 eval_cascade.py \\
      --adapters train/lora_adapter_v6_claude train/lora_adapter_v7_claude train/lora_adapter_v8_claude \\
      --sample-n-per-adapter 4 --sample-temp 0.7 \\
      --out bench/results/.../agc-cascade.jsonl
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


def extract_sig_hints(tests_ag: str) -> str:
    seen: dict[str, int] = {}
    pat = re.compile(r"\((?:assert-eq|assert-near|eq\?|near\?)\s+\(([a-z_][a-z0-9_]*)\b([^)]*?)\)")
    for m in pat.finditer(tests_ag):
        name, argstr = m.group(1), m.group(2)
        depth = 0
        in_str = False
        toks = 0
        cur = ""
        for ch in argstr.strip():
            if in_str:
                if ch == '"':
                    in_str = False
                cur += ch
                continue
            if ch == '"':
                in_str = True
                cur += ch
            elif ch == "(":
                depth += 1
                cur += ch
            elif ch == ")":
                depth -= 1
                cur += ch
            elif ch.isspace() and depth == 0:
                if cur:
                    toks += 1
                    cur = ""
            else:
                cur += ch
        if cur:
            toks += 1
        if name not in seen:
            seen[name] = toks
    if not seen:
        return ""
    return "Function signatures (from the test calls):\n" + "\n".join(
        f"  - `{n}` takes {a} argument(s)" for n, a in seen.items())


def build_prompt(obj: str, tests_ag: str) -> str:
    sig = extract_sig_hints(tests_ag)
    return (f"Write an AGC module that satisfies this objective:\n\n{obj}\n\n"
            + (f"{sig}\n\n" if sig else "")
            + f"The module must include these (test ...) blocks:\n\n{tests_ag}\n\n"
            + "Output ONLY the module source.")


def chat_format(tokenizer, system: str, user: str) -> str:
    msgs = [{"role": "system", "content": system}, {"role": "user", "content": user}]
    return tokenizer.apply_chat_template(msgs, tokenize=False, add_generation_prompt=True)


def extract_module(text: str) -> str | None:
    end = text.find("<|im_end|>")
    if end != -1:
        text = text[:end]
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
                return text[start:i + 1]
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
    finally:
        try:
            os.unlink(path)
        except Exception:
            pass


def count_loc(source: str) -> int:
    return sum(1 for line in source.splitlines() if line.strip())


def gen_one(model, tok, prompt: str, temp: float, max_tokens: int) -> tuple[str | None, int, int]:
    chat = chat_format(tok, SYSTEM_PROMPT, prompt)
    in_tok = len(tok.encode(chat))
    out = generate(model, tok, prompt=chat, max_tokens=max_tokens,
                   sampler=make_sampler(temp=temp, top_p=0.95), verbose=False)
    out_tok = len(tok.encode(out))
    mod = extract_module(out)
    return mod, in_tok, out_tok


def solve(p: Path, models: list, max_tokens: int, sample_n_per: int, sample_temp: float) -> dict:
    obj = (p / "objective.md").read_text().strip()
    tests = (p / "tests.ag").read_text().strip()
    prompt = build_prompt(obj, tests)

    tin = 0
    tout = 0
    samples = 0
    best_mod = None
    best_pv = 0
    best_tv = 0

    # Phase 1: greedy on each adapter in priority order
    for idx, (label, model, tok) in enumerate(models):
        mod, ti, to = gen_one(model, tok, prompt, 0.0, max_tokens)
        tin += ti
        tout += to
        samples += 1
        if mod is None:
            continue
        ok, pv, tv, _ = verify(mod)
        if ok:
            return _result(True, mod, f"greedy-{label}", samples, tin, tout, pv, tv)
        if (pv / max(1, tv)) > (best_pv / max(1, best_tv)):
            best_mod, best_pv, best_tv = mod, pv, tv

    # Phase 2: sampling on each adapter
    for label, model, tok in models:
        for k in range(sample_n_per):
            mod, ti, to = gen_one(model, tok, prompt, sample_temp, max_tokens)
            tin += ti
            tout += to
            samples += 1
            if mod is None:
                continue
            ok, pv, tv, _ = verify(mod)
            if ok:
                return _result(True, mod, f"sample-{label}", samples, tin, tout, pv, tv)
            if (pv / max(1, tv)) > (best_pv / max(1, best_tv)):
                best_mod, best_pv, best_tv = mod, pv, tv

    return _result(False, best_mod, "exhausted", samples, tin, tout, best_pv, best_tv)


def _result(passed, mod, stage, samples, tin, tout, pv, tv):
    return {
        "pass": passed,
        "stage": stage,
        "samples": samples,
        "tokens_in": tin,
        "tokens_out": tout,
        "best_module": mod,
        "tests_passed": pv,
        "tests_total": tv,
        "loc": count_loc(mod) if mod else 0,
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default="mlx-community/Qwen2.5-Coder-3B-Instruct-4bit")
    ap.add_argument("--adapters", nargs="+", required=True,
                    help="adapter dirs in priority order (cheapest/most-token-efficient first)")
    ap.add_argument("--sample-n-per-adapter", type=int, default=4)
    ap.add_argument("--sample-temp", type=float, default=0.7)
    ap.add_argument("--max-tokens", type=int, default=1200)
    ap.add_argument("--only", help="comma-separated problem prefixes")
    ap.add_argument("--out", help="output jsonl path")
    args = ap.parse_args()

    models = []
    for a in args.adapters:
        label = Path(a).name.replace("lora_adapter_", "")
        print(f"Loading [{label}] from {a}...", flush=True)
        m, t = load(args.model, adapter_path=a)
        models.append((label, m, t))
    print(f"Loaded {len(models)} adapters.", flush=True)

    date = time.strftime("%Y-%m-%d")
    out_path = Path(args.out) if args.out else BENCH_RESULTS / date / "agc-cascade.jsonl"
    out_path.parent.mkdir(parents=True, exist_ok=True)

    problems = sorted(q for q in BENCH_PROBLEMS.iterdir() if q.is_dir())
    if args.only:
        keys = args.only.split(",")
        problems = [q for q in problems if any(q.name.startswith(k) for k in keys)]

    records = []
    passed = 0
    by_stage: dict[str, int] = {}
    t_start = time.monotonic()

    for q in problems:
        t0 = time.monotonic()
        r = solve(q, models, args.max_tokens, args.sample_n_per_adapter, args.sample_temp)
        wall = time.monotonic() - t0

        rec = {
            "id": q.name,
            "track": "agc-cascade",
            "pass": r["pass"],
            "stage": r["stage"],
            "samples": r["samples"],
            "wall_time_s": wall,
            "tokens_in": r["tokens_in"],
            "tokens_out": r["tokens_out"],
            "source_loc": r["loc"],
            "tests_passed": r["tests_passed"],
            "tests_total": r["tests_total"],
            "capabilities": [],
            "decomposition_depth": 0,
        }
        records.append(rec)
        if r["pass"]:
            passed += 1
            by_stage[r["stage"]] = by_stage.get(r["stage"], 0) + 1
        status = "PASS" if r["pass"] else "FAIL"
        print(f"[{status}] {q.name:30} stage={r['stage']:18} samples={r['samples']} "
              f"wall={wall:.1f}s toks={rec['tokens_in']+rec['tokens_out']} "
              f"tests={r['tests_passed']}/{r['tests_total']}", flush=True)

    with out_path.open("w") as fh:
        for rec in records:
            fh.write(json.dumps(rec) + "\n")

    total = time.monotonic() - t_start
    print("\n=== SUMMARY ===")
    print(f"pass: {passed}/{len(records)} ({100 * passed / len(records):.0f}%)")
    print(f"wall: {total:.0f}s")
    for s, n in sorted(by_stage.items()):
        print(f"  {s}: {n}")
    total_toks = sum(r["tokens_in"] + r["tokens_out"] for r in records)
    print(f"total tokens: {total_toks}, tokens/pass: {total_toks // max(1, passed)}")
    print(f"results: {out_path}")


if __name__ == "__main__":
    sys.exit(main() or 0)
