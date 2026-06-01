#!/usr/bin/env python3
"""Augment a curated AGC dataset with no-tests variants for L1+L2.

Each input row {objective, solution, ...} → two output rows:
  1. format="with_tests"  — original prompt + original full-module solution
  2. format="no_tests"    — new prompt + solution stripped to defuns/externs only

Downstream `train/finetune_mlx.py:to_chat()` switches on `format` to pick
the right user prompt for each example.

Usage:
  python3 train/augment_no_tests.py \
    --in  train/dataset/agc_pairs_claude.jsonl \
    --out train/dataset/agc_pairs_claude_dual.jsonl
"""
from __future__ import annotations
import argparse, json, sys
from pathlib import Path


def strip_im_end(text: str) -> str:
    end = text.find("<|im_end|>")
    return text[:end] if end != -1 else text


def extract_defuns(text: str) -> str | None:
    """Pull top-level (defun…)/(extern…)/(defstruct…)/(def …) forms out of a
    full module solution. Discards (module …) wrapper and (test …) blocks.
    Returns None if nothing parseable is found.

    Matches the extractor used by the inference-time pipeline in
    `train/probe_no_tests_prompt.py`, so the model is trained on exactly
    the format the eval pipeline will accept.
    """
    text = strip_im_end(text)
    mod_start = text.find("(module")
    if mod_start != -1:
        head_end = mod_start + len("(module")
        first_inner = text.find("(", head_end)
        if first_inner != -1:
            text = text[first_inner:]

    out: list[str] = []
    i, n = 0, len(text)
    while i < n:
        if text[i] != "(":
            i += 1; continue
        j = i + 1
        while j < n and text[j].isspace():
            j += 1
        head_start = j
        while j < n and not text[j].isspace() and text[j] != "(" and text[j] != ")":
            j += 1
        head = text[head_start:j]
        if head not in ("defun", "extern", "defstruct", "def"):
            i += 1; continue
        depth = 0; k = i; in_str = False; escape = False
        while k < n:
            ch = text[k]
            if in_str:
                if escape: escape = False
                elif ch == "\\": escape = True
                elif ch == '"': in_str = False
            else:
                if ch == '"': in_str = True
                elif ch == "(": depth += 1
                elif ch == ")":
                    depth -= 1
                    if depth == 0:
                        out.append(text[i:k+1])
                        i = k + 1
                        break
            k += 1
        else:
            return None
    return "\n\n".join(out) if out else None


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--in", dest="in_path", required=True, type=Path)
    ap.add_argument("--out", dest="out_path", required=True, type=Path)
    args = ap.parse_args()

    n_in, n_out, n_skipped_unstrippable = 0, 0, 0
    with args.in_path.open() as fin, args.out_path.open("w") as fout:
        for line in fin:
            line = line.strip()
            if not line: continue
            n_in += 1
            rec = json.loads(line)

            # 1. with-tests variant — original record, tagged.
            with_tests = dict(rec)
            with_tests["format"] = "with_tests"
            fout.write(json.dumps(with_tests) + "\n")
            n_out += 1

            # 2. no-tests variant — solution stripped to defuns/externs only.
            stripped = extract_defuns(rec["solution"])
            if stripped is None:
                n_skipped_unstrippable += 1
                continue
            no_tests = dict(rec)
            no_tests["solution"] = stripped
            no_tests["format"] = "no_tests"
            fout.write(json.dumps(no_tests) + "\n")
            n_out += 1

    print(f"in:                       {n_in} pairs")
    print(f"out:                      {n_out} pairs ({n_out - n_in} new no-tests variants)")
    print(f"skipped (unstrippable):   {n_skipped_unstrippable}")
    print(f"out path:                 {args.out_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
