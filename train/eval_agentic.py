#!/usr/bin/env python3
"""Agentic verifier-feedback eval.

For each problem:
  1. Sample one candidate at temp=0 (greedy — most token-efficient).
  2. Run agc check.
  3. If pass: done (return early with attempt 1 token cost).
  4. If fail: build a tight retry prompt with (a) the previous module
     and (b) the relevant verifier diagnostics. Sample at temp=0.5.
  5. Repeat up to MAX_ATTEMPTS.

Each retry is an INDEPENDENT prompt (we don't accumulate the chat history,
which would balloon tokens-in). The verifier error itself is the signal.

Records per problem: pass, attempts, tokens_in, tokens_out, transcript.

Usage:
  python3 eval_agentic.py --adapter train/lora_adapter_v6_claude \\
      [--max-attempts 5] [--only 13,14] [--out path/to.jsonl]
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
DIAG_MSG_RE = re.compile(r'\(diagnostic[^)]*\(message "([^"]+)"')


# ============== prompt construction ==============


def extract_signature_hints(tests_ag: str) -> str:
    seen: dict[str, int] = {}
    pattern = re.compile(r"\((?:assert-eq|assert-near|eq\?|near\?)\s+\(([a-z_][a-z0-9_]*)\b([^)]*?)\)")
    for m in pattern.finditer(tests_ag):
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
    lines = [f"  - `{n}` takes {a} argument(s)" for n, a in seen.items()]
    return "Function signatures (from the test calls):\n" + "\n".join(lines)


def build_initial_prompt(objective: str, tests_ag: str) -> str:
    sig = extract_signature_hints(tests_ag)
    return (f"Write an AGC module that satisfies this objective:\n\n{objective}\n\n"
            + (f"{sig}\n\n" if sig else "")
            + f"The module must include these (test ...) blocks:\n\n{tests_ag}\n\n"
            + "Output ONLY the module source.")


def build_retry_prompt(objective: str, tests_ag: str, prev_module: str, err_text: str) -> str:
    """Tight retry: previous attempt + verifier diagnostics + fix instruction.

    We keep the test block verbatim so the model can re-read the exact contract
    and re-anchor on test signatures (sometimes the first attempt drifted).
    """
    diag_msgs = DIAG_MSG_RE.findall(err_text)
    if diag_msgs:
        # Dedupe while preserving order, cap at 5 messages, truncate each.
        seen = set()
        unique = []
        for m in diag_msgs:
            if m not in seen and len(unique) < 5:
                unique.append(m)
                seen.add(m)
        err_summary = "\n".join(f"- {m[:300]}" for m in unique)
    else:
        err_summary = err_text.strip()[-500:]

    sig = extract_signature_hints(tests_ag)
    return (
        f"Your previous AGC module failed verification. Fix the specific errors below.\n\n"
        f"Objective:\n{objective}\n\n"
        + (f"{sig}\n\n" if sig else "")
        + f"Test contract:\n{tests_ag}\n\n"
        f"Verification errors:\n{err_summary}\n\n"
        f"Previous attempt:\n{prev_module}\n\n"
        "Output a corrected complete (module …) — only the source, nothing else."
    )


def chat_format(tokenizer, system: str, user: str) -> str:
    msgs = [{"role": "system", "content": system}, {"role": "user", "content": user}]
    return tokenizer.apply_chat_template(msgs, tokenize=False, add_generation_prompt=True)


# ============== module extraction + verification ==============


def extract_module(text: str) -> str | None:
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
            return False, int(m.group(1)), int(m.group(2)), out[-1500:]
        return False, 0, 0, out[-1500:]
    except subprocess.TimeoutExpired:
        return False, 0, 0, "timeout"
    finally:
        try:
            os.unlink(path)
        except Exception:
            pass


def count_loc(source: str) -> int:
    return sum(1 for line in source.splitlines() if line.strip())


# ============== agentic loop ==============


def solve(problem_dir: Path, model, tokenizer, max_attempts: int,
          retry_temp: float, max_tokens: int) -> dict:
    obj = (problem_dir / "objective.md").read_text().strip()
    tests_ag = (problem_dir / "tests.ag").read_text().strip()

    transcript = []
    total_in = 0
    total_out = 0
    prev_module: str | None = None
    prev_err: str = ""

    for attempt in range(1, max_attempts + 1):
        if attempt == 1:
            user = build_initial_prompt(obj, tests_ag)
            sampler = make_sampler(temp=0.0, top_p=0.95)
        else:
            user = build_retry_prompt(obj, tests_ag, prev_module or "(no parseable module)", prev_err)
            # Slight diversity for retries to escape stuck patterns.
            sampler = make_sampler(temp=retry_temp, top_p=0.95)

        chat = chat_format(tokenizer, SYSTEM_PROMPT, user)
        in_toks = len(tokenizer.encode(chat))

        out = generate(model, tokenizer, prompt=chat, max_tokens=max_tokens,
                       sampler=sampler, verbose=False)
        out_toks = len(tokenizer.encode(out))

        total_in += in_toks
        total_out += out_toks

        module = extract_module(out)
        if module is None:
            ok, p, t, err = False, 0, 0, "parse-error: no (module ...) form found"
        else:
            ok, p, t, err = verify(module)

        transcript.append({
            "attempt": attempt,
            "tokens_in": in_toks,
            "tokens_out": out_toks,
            "tests_passed": p,
            "tests_total": t,
            "ok": ok,
            "module": module,
            "err_excerpt": (err[:600] if err else ""),
        })

        if ok:
            return {
                "pass": True,
                "attempts": attempt,
                "tokens_in": total_in,
                "tokens_out": total_out,
                "tests_passed": p,
                "tests_total": t,
                "best_module": module,
                "loc": count_loc(module),
                "transcript": transcript,
            }

        prev_module = module if module else "(no parseable module)"
        prev_err = err

    # Out of attempts
    last = transcript[-1]
    return {
        "pass": False,
        "attempts": max_attempts,
        "tokens_in": total_in,
        "tokens_out": total_out,
        "tests_passed": last["tests_passed"],
        "tests_total": last["tests_total"],
        "best_module": last["module"],
        "loc": count_loc(last["module"]) if last["module"] else 0,
        "transcript": transcript,
    }


# ============== driver ==============


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default="mlx-community/Qwen2.5-Coder-3B-Instruct-4bit")
    ap.add_argument("--adapter", default=str(TRAIN_DIR / "lora_adapter_v6_claude"))
    ap.add_argument("--only", help="comma-sep problem prefixes")
    ap.add_argument("--out", help="override output jsonl path")
    ap.add_argument("--max-attempts", type=int, default=5)
    ap.add_argument("--retry-temp", type=float, default=0.5)
    ap.add_argument("--max-tokens", type=int, default=1200)
    ap.add_argument("--save-transcripts", action="store_true",
                    help="also write full per-problem transcripts")
    args = ap.parse_args()

    print(f"Loading {args.model} + adapter {args.adapter}...", flush=True)
    model, tokenizer = load(args.model, adapter_path=args.adapter)
    print("Loaded.", flush=True)

    date = time.strftime("%Y-%m-%d")
    out_path = Path(args.out) if args.out else BENCH_RESULTS / date / "agc-agentic.jsonl"
    out_path.parent.mkdir(parents=True, exist_ok=True)
    transcripts_path = out_path.with_suffix(".transcripts.jsonl")

    problems = sorted(p for p in BENCH_PROBLEMS.iterdir() if p.is_dir())
    if args.only:
        keys = args.only.split(",")
        problems = [p for p in problems if any(p.name.startswith(k) for k in keys)]

    records = []
    transcripts = []
    passed = 0
    t_start = time.monotonic()

    for p in problems:
        t0 = time.monotonic()
        result = solve(p, model, tokenizer, args.max_attempts, args.retry_temp, args.max_tokens)
        wall = time.monotonic() - t0

        rec = {
            "id": p.name,
            "track": "agc-agentic",
            "pass": result["pass"],
            "attempts": result["attempts"],
            "wall_time_s": wall,
            "tokens_in": result["tokens_in"],
            "tokens_out": result["tokens_out"],
            "source_loc": result["loc"],
            "capabilities": [],
            "decomposition_depth": 0,
            "tests_passed": result["tests_passed"],
            "tests_total": result["tests_total"],
            "error_category": None if result["pass"] else "test-fail",
        }
        records.append(rec)
        transcripts.append({"id": p.name, "transcript": result["transcript"]})

        if result["pass"]:
            passed += 1
        status = "PASS" if result["pass"] else "FAIL"
        print(f"[{status}] {p.name:30} att={result['attempts']} wall={wall:.1f}s "
              f"toks={result['tokens_in'] + result['tokens_out']} "
              f"tests={result['tests_passed']}/{result['tests_total']} "
              f"loc={result['loc']}", flush=True)

    with out_path.open("w") as fh:
        for r in records:
            fh.write(json.dumps(r) + "\n")
    if args.save_transcripts:
        with transcripts_path.open("w") as fh:
            for r in transcripts:
                fh.write(json.dumps(r) + "\n")

    total = time.monotonic() - t_start
    print("\n=== SUMMARY ===")
    print(f"pass: {passed}/{len(records)} ({100 * passed / len(records):.0f}%)")
    print(f"total wall: {total:.0f}s")

    # Token efficiency breakdown
    one_shot_pass = sum(1 for r in records if r["pass"] and r["attempts"] == 1)
    retry_pass = passed - one_shot_pass
    one_shot_toks = sum(r["tokens_in"] + r["tokens_out"]
                        for r in records if r["pass"] and r["attempts"] == 1)
    retry_toks = sum(r["tokens_in"] + r["tokens_out"]
                     for r in records if r["pass"] and r["attempts"] > 1)
    fail_toks = sum(r["tokens_in"] + r["tokens_out"]
                    for r in records if not r["pass"])
    total_toks = one_shot_toks + retry_toks + fail_toks
    print(f"  one-shot passes: {one_shot_pass}, tokens={one_shot_toks} "
          f"(median per pass: {sorted([r['tokens_in']+r['tokens_out'] for r in records if r['pass'] and r['attempts']==1])[max(0,one_shot_pass//2)] if one_shot_pass else 0})")
    print(f"  retry passes: {retry_pass}, tokens={retry_toks}")
    print(f"  failed: {len(records) - passed}, tokens burned={fail_toks}")
    print(f"  TOTAL tokens: {total_toks}, tokens/pass: {total_toks // max(1, passed)}")
    print(f"results: {out_path}")
    if args.save_transcripts:
        print(f"transcripts: {transcripts_path}")


if __name__ == "__main__":
    sys.exit(main() or 0)
