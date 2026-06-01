#!/usr/bin/env python3
"""Extend Qwen's tokenizer with AGC-specific tokens (C3).

Reads candidates from train_agc_bpe.py's raw output, applies the
whitelist filter, deduplicates against Qwen's existing vocab, assigns
new IDs starting at Qwen's total vocab size, writes a HF-compatible
tokenizer.json that AutoTokenizer.from_pretrained can load.

Decision gate: if fewer than 200 net-new tokens survive, prints a
warning and exits non-zero so a human can decide whether to proceed.

Usage:
  python3 train/extend_qwen_tokenizer.py \
    --raw   train/agc_tokenizer/raw_bpe.json \
    --qwen  mlx-community/Qwen2.5-Coder-7B-Instruct-4bit \
    --out   train/agc_tokenizer/ \
    --min-new 200
"""
from __future__ import annotations
import argparse
import json
import sys
from pathlib import Path
from typing import Sequence

from agc_stdlib_tokens import AGC_STDLIB
from train_agc_bpe import is_valid_agc_token

TRAIN_DIR = Path(__file__).resolve().parent
DEFAULT_RAW = TRAIN_DIR / "agc_tokenizer" / "raw_bpe.json"
DEFAULT_OUT_DIR = TRAIN_DIR / "agc_tokenizer"
DEFAULT_QWEN = "mlx-community/Qwen2.5-Coder-7B-Instruct-4bit"


def filter_new_tokens(candidates: Sequence[str], qwen_vocab: dict[str, int]) -> list[str]:
    """Drop any candidate already present in `qwen_vocab`. Preserve order."""
    return [c for c in candidates if c not in qwen_vocab]


def assign_new_ids(candidates: Sequence[str], start_id: int) -> dict[str, int]:
    """Assign sequential IDs starting at `start_id`. Order-preserving."""
    return {c: start_id + i for i, c in enumerate(candidates)}


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--raw", type=Path, default=DEFAULT_RAW)
    ap.add_argument("--qwen", default=DEFAULT_QWEN)
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT_DIR)
    ap.add_argument("--min-new", type=int, default=200,
                    help="Decision gate: minimum acceptable net-new token count.")
    args = ap.parse_args()

    # 1. Load Qwen tokenizer's full vocab + tokenizer.json
    from transformers import AutoTokenizer
    qwen_tok = AutoTokenizer.from_pretrained(args.qwen)
    qwen_vocab = qwen_tok.get_vocab()
    qwen_total = max(qwen_vocab.values()) + 1
    print(f"Qwen vocab size (incl special): {qwen_total}")

    # 2. Load raw BPE candidates
    raw = json.loads(args.raw.read_text())
    raw_vocab = raw["model"]["vocab"]
    candidates_in_order = [t for t, _ in sorted(raw_vocab.items(), key=lambda kv: kv[1])]

    # 3. Apply whitelist filter
    after_whitelist = [t for t in candidates_in_order if is_valid_agc_token(t, AGC_STDLIB)]
    print(f"After whitelist: {len(after_whitelist)} / {len(candidates_in_order)}")

    # 4. Deduplicate against Qwen
    new_only = filter_new_tokens(after_whitelist, qwen_vocab)
    print(f"After dedup vs Qwen: {len(new_only)} (dropped "
          f"{len(after_whitelist) - len(new_only)} already in Qwen)")

    # 5. Decision gate
    if len(new_only) < args.min_new:
        print(f"WARNING: only {len(new_only)} new tokens, below threshold {args.min_new}.")
        print("Pause and adjust BPE settings before proceeding.")
        return 1

    # 6. Assign new IDs
    id_map = assign_new_ids(new_only, qwen_total)

    # 7. Save extended tokenizer (use add_tokens API, save_pretrained)
    qwen_tok.add_tokens(new_only)
    args.out.mkdir(parents=True, exist_ok=True)
    qwen_tok.save_pretrained(str(args.out))

    # 8. Save id_map as a sidecar for downstream embedding patching
    (args.out / "new_token_id_map.json").write_text(
        json.dumps(id_map, indent=2, ensure_ascii=False)
    )
    print(f"Wrote extended tokenizer to {args.out}")
    print(f"New token count: {len(new_only)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
