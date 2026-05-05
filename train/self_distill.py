#!/usr/bin/env python3
"""Self-distillation harvester.

Runs the local fine-tuned model with Best-of-N over a list of objectives,
verifies each candidate via `agc check`, and writes every passing module
to a dataset jsonl. Output is in the same format as generate_dataset.py
so the corpus can be merged for the next training cycle.

Why this works: verifier-filtered self-samples teach the model to
double-down on patterns IT can actually produce. Pro distillation gives
us the right "shape" of code; self-distillation polishes the in-distribution
patterns. Compounding effect on per-sample success probability.

Usage:
  python3 self_distill.py \
    --topics-file train/topics_diverse.txt \
    --n 16 --temp 0.8 \
    --out train/dataset/agc_pairs_self.jsonl
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
AGC_CLI_DLL = REPO_ROOT / "Agentic.Cli" / "bin" / "Debug" / "net8.0" / "Agentic.Cli.dll"

SYSTEM_PROMPT = "Output only AGC (Agentic Compiler) S-expression source code, no prose."

TESTS_OK_RE = re.compile(r"\(ok \(tests-passed (\d+)/(\d+)\)\)")


def chat_format(tokenizer, system: str, user: str) -> str:
    msgs = [{"role": "system", "content": system}, {"role": "user", "content": user}]
    return tokenizer.apply_chat_template(msgs, tokenize=False, add_generation_prompt=True)


def extract_module(text: str) -> str | None:
    end = text.find("<|im_end|>")
    if end != -1:
        text = text[:end]
    start = text.find("(module")
    if start == -1: return None
    depth = 0; in_str = False; esc = False
    for i in range(start, len(text)):
        ch = text[i]
        if in_str:
            if esc: esc = False
            elif ch == "\\": esc = True
            elif ch == '"': in_str = False
            continue
        if ch == '"': in_str = True
        elif ch == "(": depth += 1
        elif ch == ")":
            depth -= 1
            if depth == 0: return text[start:i+1]
    return None


def verify(source: str) -> tuple[bool, int, int]:
    with tempfile.NamedTemporaryFile("w", suffix=".ag", delete=False, dir="/tmp") as f:
        f.write(source); path = f.name
    try:
        proc = subprocess.run(
            ["dotnet", str(AGC_CLI_DLL), "check", path,
             "--allow-env", "--allow-file", "--allow-http", "--allow-db"],
            capture_output=True, text=True, timeout=20)
        out = proc.stdout + "\n" + proc.stderr
        m = TESTS_OK_RE.search(out)
        if m and int(m.group(2)) > 0:
            return True, int(m.group(1)), int(m.group(2))
        return False, 0, 0
    except subprocess.TimeoutExpired:
        return False, 0, 0
    finally:
        try: os.unlink(path)
        except Exception: pass


def categorise(topic: str) -> str:
    t = topic.lower()
    if any(w in t for w in ("env var", "file", "http", "db ", "database",
                            "json", "process", "endpoint")):
        return "cap"
    if any(w in t for w in ("multi", "compute monthly", "loan", "payment",
                            "bill", "cart", "tax", "ride", "shipping",
                            "compound", "amortizing", "subscription",
                            "discount", "currency", "bracket")):
        return "multi"
    return "pure"


def build_prompt(objective: str) -> str:
    """Match the training prompt format — no test specification."""
    return (f"Write an AGC module that satisfies this objective:\n\n{objective}\n\n"
            f"Include self-verifying (test ...) blocks. Output ONLY the module source.")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default="mlx-community/Qwen2.5-Coder-3B-Instruct-4bit")
    ap.add_argument("--adapter", default=str(TRAIN_DIR / "lora_adapter"))
    ap.add_argument("--topics-file",
                    help="newline-separated topics to harvest from (default: re-uses generate_dataset topics)")
    ap.add_argument("--n", type=int, default=16, help="candidates per objective")
    ap.add_argument("--temp", type=float, default=0.8)
    ap.add_argument("--max-tokens", type=int, default=600)
    ap.add_argument("--target", type=int, default=1000,
                    help="harvest until this many verified pairs")
    ap.add_argument("--out", default=str(TRAIN_DIR / "dataset" / "agc_pairs_self.jsonl"))
    args = ap.parse_args()

    # Topics: re-use generate_dataset's lists by importing it.
    if args.topics_file:
        topics = [t.strip() for t in Path(args.topics_file).read_text().splitlines() if t.strip()]
    else:
        sys.path.insert(0, str(TRAIN_DIR))
        from generate_dataset import PURE_TOPICS, CAP_TOPICS, MULTI_TOPICS
        topics = list(set(PURE_TOPICS + CAP_TOPICS + MULTI_TOPICS))

    print(f"Loading {args.model}...", flush=True)
    model, tokenizer = load(args.model, adapter_path=args.adapter)
    print(f"Loaded. Harvesting from {len(topics)} topics, target={args.target}.", flush=True)

    out_path = Path(args.out)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    # If file exists, append (resume-friendly)
    seen = set()
    verified = 0
    if out_path.exists():
        for line in out_path.open():
            d = json.loads(line)
            seen.add(d["solution"])
            verified += 1
        print(f"Resuming with {verified} pairs already harvested", flush=True)

    import random
    random.shuffle(topics)
    t0 = time.monotonic()

    for topic_idx, topic in enumerate(topics):
        if verified >= args.target:
            break
        cat = categorise(topic)
        objective = f"{topic.capitalize()}: write a small AGC module."
        prompt = build_prompt(objective)
        chat_prompt = chat_format(tokenizer, SYSTEM_PROMPT, prompt)

        # Sample N candidates with diversity
        for k in range(args.n):
            if verified >= args.target: break
            sampler = make_sampler(temp=args.temp, top_p=0.95)
            text = generate(model, tokenizer, prompt=chat_prompt,
                            max_tokens=args.max_tokens, sampler=sampler, verbose=False)
            module = extract_module(text)
            if module is None or module in seen:
                continue
            ok, p, t = verify(module)
            if not ok:
                continue
            seen.add(module)
            verified += 1
            with out_path.open("a") as fh:
                fh.write(json.dumps({
                    "category": cat, "topic": topic,
                    "objective": objective, "solution": module,
                    "tests_passed": p, "source": "self-distill",
                }) + "\n")
            rate = verified / ((time.monotonic() - t0) / 60 + 1e-9)
            if verified % 10 == 0:
                print(f"  verified={verified}/{args.target} topic#{topic_idx} "
                      f"rate={rate:.1f}/min", flush=True)

    dt = (time.monotonic() - t0) / 60
    print(f"\nDone: {verified}/{args.target} in {dt:.1f} min")
    print(f"  out: {out_path}")


if __name__ == "__main__":
    sys.exit(main() or 0)
