#!/usr/bin/env python3
"""Train a BPE on AGC-only corpus, output candidate tokens (C2).

Uses HuggingFace `tokenizers` with the same byte-level pre-tokenizer
config Qwen uses, so resulting merges are byte-compatible.

A whitelist filter keeps only candidates that are real AGC syntax
(stdlib calls, keywords, type annotations, operators) and rejects
user-defined identifiers from the corpus.

Usage:
  python3 train/train_agc_bpe.py \
    --in  train/dataset/agc_corpus.txt \
    --out train/agc_tokenizer/ \
    --vocab-size 500
"""
from __future__ import annotations
import argparse
import json
import re
from pathlib import Path

from agc_stdlib_tokens import AGC_STDLIB

TRAIN_DIR = Path(__file__).resolve().parent
DEFAULT_IN = TRAIN_DIR / "dataset" / "agc_corpus.txt"
DEFAULT_OUT_DIR = TRAIN_DIR / "agc_tokenizer"

# Tokens with these prefixes are s-expr openers — keep regardless of length.
SEXPR_PREFIXES = ("(", ")")
# Type-annotation prefixes
TYPE_PREFIXES = (": ", "-> ")


def is_valid_agc_token(token: str, stdlib: set[str]) -> bool:
    """Return True if `token` should be kept as an AGC vocab extension."""
    if not token:
        return False
    # Strip any leading byte-level marker (Qwen uses 'Ġ' for leading space)
    bare = token.lstrip("Ġ").lstrip()
    if not bare:
        return False
    # Keep s-expr openers like "(defun", "(assert-eq", "(extern"
    if bare.startswith("("):
        inner = bare[1:]
        if inner in stdlib:
            return True
        # Short opener like "(do" is OK
        if len(inner) <= 4 and re.match(r"^[a-zA-Z\-?!*+]+$", inner):
            return True
        return False
    # Keep type annotations
    for pref in TYPE_PREFIXES:
        if bare.startswith(pref):
            ann = bare[len(pref):]
            if ann in stdlib:
                return True
    # Keep stdlib symbols verbatim
    if bare in stdlib:
        return True
    # Keep short tokens (≤4 chars) that look like operators / keywords
    # (AGC keywords/operators are lowercase; mixed-case is rejected as a fragment)
    if len(bare) <= 4 and re.match(r"^[a-z\-?!*+=<>]+$", bare):
        return True
    # Reject everything else (user identifiers, random fragments)
    return False


def train_bpe(corpus_path: Path, vocab_size: int, out_dir: Path) -> Path:
    """Train BPE on `corpus_path`, save raw tokenizer.json to out_dir.

    Returns the path to the saved raw tokenizer.json (filtering happens
    in extend_qwen_tokenizer.py).
    """
    from tokenizers import Tokenizer, models, trainers, pre_tokenizers

    out_dir.mkdir(parents=True, exist_ok=True)
    tok = Tokenizer(models.BPE(unk_token="<unk>"))
    tok.pre_tokenizer = pre_tokenizers.ByteLevel(add_prefix_space=False)
    trainer = trainers.BpeTrainer(
        vocab_size=vocab_size,
        special_tokens=["<unk>"],
        initial_alphabet=pre_tokenizers.ByteLevel.alphabet(),
        show_progress=True,
    )
    tok.train(files=[str(corpus_path)], trainer=trainer)
    raw_path = out_dir / "raw_bpe.json"
    tok.save(str(raw_path))
    return raw_path


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--in", dest="corpus_in", type=Path, default=DEFAULT_IN)
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT_DIR)
    ap.add_argument("--vocab-size", type=int, default=500)
    args = ap.parse_args()

    raw_path = train_bpe(args.corpus_in, args.vocab_size, args.out)

    # Print top-50 candidates after applying the whitelist filter (sanity preview).
    with raw_path.open() as f:
        raw = json.load(f)
    vocab = raw["model"]["vocab"]
    sorted_tokens = sorted(vocab.items(), key=lambda kv: kv[1])
    kept = [t for t, _ in sorted_tokens if is_valid_agc_token(t, AGC_STDLIB)]
    print(f"Trained BPE with vocab_size={args.vocab_size}; "
          f"kept {len(kept)}/{len(vocab)} after whitelist filter.")
    print("Top 50 kept candidates:")
    for t in kept[:50]:
        print(f"  {t!r}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
