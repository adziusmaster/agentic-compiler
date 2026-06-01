#!/usr/bin/env python3
"""Generate AGC vocabulary extension candidates directly from the curated
stdlib list, bypassing BPE.

Why bypass BPE: BPE on AGC corpus produces fragments that overlap heavily
with Qwen's existing vocab (Qwen 2.5 Coder was trained on huge code+English
corpora and already knows most common short fragments). We have a curated
list of AGC-specific symbols; emit them directly with the surface forms
we want as single tokens.

Output format matches what extend_qwen_tokenizer.py reads:
  {"model": {"vocab": {token_string: id_int, ...}}}

Usage:
  python3 train/build_agc_tokens.py --out train/agc_tokenizer/raw_bpe.json
"""
from __future__ import annotations
import argparse
import json
from pathlib import Path

from agc_stdlib_tokens import (
    AGC_KEYWORDS, AGC_TYPES, AGC_STDLIB_CALLS, AGC_ASSERTIONS,
)

TRAIN_DIR = Path(__file__).resolve().parent
DEFAULT_OUT = TRAIN_DIR / "agc_tokenizer" / "raw_bpe.json"


def generate_candidates() -> list[str]:
    """Generate the full set of candidate token strings, deduplicated, ordered."""
    cands: list[str] = []
    seen: set[str] = set()

    def add(token: str) -> None:
        if token and token not in seen:
            seen.add(token)
            cands.append(token)

    # 1. Each stdlib symbol in three forms: bare, leading-space, paren-prefixed
    all_symbols = list(AGC_KEYWORDS) + list(AGC_TYPES) + list(AGC_STDLIB_CALLS) + list(AGC_ASSERTIONS)
    # Sort for deterministic ordering
    for sym in sorted(set(all_symbols)):
        add(sym)               # bare
        add("Ġ" + sym)    # leading-space (Ġ in BBPE)
        add("(" + sym)         # s-expr opener

    # 2. Type-annotation tokens (highly compressible — Qwen splits these into 2-3 tokens)
    for typ in sorted(AGC_TYPES):
        add(": " + typ)
        add("-> " + typ)
        add("Ġ: " + typ)
        add("Ġ-> " + typ)

    # 3. Boolean-ish predicate operators (unusual punctuation; unlikely in Qwen)
    for op in ["eq?", "lt?", "gt?", "le?", "ge?", "not?", "and?", "or?", "neq?",
               "list_empty?"]:
        add(op)
        add("Ġ" + op)
        add("(" + op)

    # 4. Numeric/IO operators that are AGC-specific compounds
    for op in ["assert-eq", "assert-near", "assert-true", "assert-false"]:
        add("(" + op)

    return cands


def write_raw_bpe(out_path: Path, candidates: list[str]) -> None:
    """Write candidates in the format extend_qwen_tokenizer.py expects."""
    out_path.parent.mkdir(parents=True, exist_ok=True)
    vocab = {tok: i for i, tok in enumerate(candidates)}
    payload = {
        "model": {"vocab": vocab},
        "_provenance": {
            "source": "build_agc_tokens.py (curated, not BPE)",
            "candidate_count": len(candidates),
        },
    }
    out_path.write_text(json.dumps(payload, indent=2, ensure_ascii=False))


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT)
    args = ap.parse_args()

    cands = generate_candidates()
    write_raw_bpe(args.out, cands)

    print(f"Wrote {len(cands)} candidate tokens to {args.out}")
    print(f"First 20:")
    for c in cands[:20]:
        print(f"  {c!r}")
    print(f"Last 20:")
    for c in cands[-20:]:
        print(f"  {c!r}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
