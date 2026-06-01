#!/usr/bin/env python3
"""Extract AGC source into a single newline-delimited corpus file (C1).

Reads `solution` fields from training-pair JSONL files and `.ag` files
from the bench tree, writes them to `train/dataset/agc_corpus.txt`,
one module per line (newlines inside modules collapsed to spaces so
each line is a self-contained module).

Usage:
  python3 train/build_agc_corpus.py [--out PATH] [--jsonl PATH ...] [--ag-root PATH]
"""
from __future__ import annotations
import argparse
import json
from pathlib import Path
from typing import Iterable, Iterator

TRAIN_DIR = Path(__file__).resolve().parent
REPO_ROOT = TRAIN_DIR.parent
DEFAULT_OUT = TRAIN_DIR / "dataset" / "agc_corpus.txt"
DEFAULT_AG_ROOT = REPO_ROOT / "bench"


def extract_solutions_from_jsonl(path: Path) -> Iterator[str]:
    """Yield the `solution` field from each line of a training-pair JSONL."""
    with path.open() as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            rec = json.loads(line)
            sol = rec.get("solution")
            if sol:
                yield sol


def extract_ag_files(root: Path) -> Iterator[str]:
    """Yield the contents of every `.ag` file under `root` (recursive)."""
    for p in sorted(root.rglob("*.ag")):
        yield p.read_text()


def _flatten(module: str) -> str:
    """Collapse internal newlines so each module is one line in the corpus."""
    return " ".join(module.split())


def build_corpus(
    out_path: Path,
    jsonl_paths: Iterable[Path],
    ag_search_root: Path | None,
) -> int:
    """Write all AGC source to `out_path`, one module per line. Returns count."""
    count = 0
    out_path.parent.mkdir(parents=True, exist_ok=True)
    with out_path.open("w") as out:
        for jp in jsonl_paths:
            for sol in extract_solutions_from_jsonl(jp):
                out.write(_flatten(sol) + "\n")
                count += 1
        if ag_search_root is not None:
            for content in extract_ag_files(ag_search_root):
                out.write(_flatten(content) + "\n")
                count += 1
    return count


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT)
    ap.add_argument(
        "--jsonl",
        type=Path,
        action="append",
        default=None,
        help="Training-pair JSONL files. May be repeated. "
             "Default: all train/dataset/agc_pairs_*.jsonl",
    )
    ap.add_argument("--ag-root", type=Path, default=DEFAULT_AG_ROOT)
    args = ap.parse_args()

    if args.jsonl is None:
        args.jsonl = sorted((TRAIN_DIR / "dataset").glob("agc_pairs_*.jsonl"))

    n = build_corpus(args.out, args.jsonl, args.ag_root)
    print(f"Wrote {n} modules to {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
