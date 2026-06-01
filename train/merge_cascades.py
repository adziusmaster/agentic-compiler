#!/usr/bin/env python3
"""Merge two cascade results: try the primary first, fall back to the
secondary on the primary's failures.

Token accounting is HONEST: the merged cost includes the full primary
attempt cost (we pay for it even when it fails) plus the secondary's
cost only on problems the primary missed.

Usage:
  python3 train/merge_cascades.py \
    --primary bench/results/.../v12-cascade.jsonl \
    --secondary bench/results/.../v11-cascade.jsonl \
    --out bench/results/.../v12-v11-merged.jsonl
"""
from __future__ import annotations
import argparse, json, sys
from pathlib import Path


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--primary", required=True, type=Path)
    ap.add_argument("--secondary", required=True, type=Path)
    ap.add_argument("--out", required=True, type=Path)
    args = ap.parse_args()

    p_rows = {r["id"]: r for r in (json.loads(l) for l in args.primary.open())}
    s_rows = {r["id"]: r for r in (json.loads(l) for l in args.secondary.open())}

    args.out.parent.mkdir(parents=True, exist_ok=True)
    merged = []
    for pid in sorted(p_rows.keys()):
        p, s = p_rows[pid], s_rows.get(pid)
        if p["pass"] or s is None:
            r = dict(p)
            r["track"] = "agc-merged-cascade"
            r["primary_passed"] = p["pass"]
            r["secondary_used"] = False
            merged.append(r)
            continue
        # Primary failed; charge full primary cost + secondary cost.
        primary_in = p.get("tokens_in") or 0
        primary_out = p.get("tokens_out") or 0
        secondary_in = s.get("tokens_in") or 0
        secondary_out = s.get("tokens_out") or 0
        r = dict(s if s["pass"] else p)
        r["track"] = "agc-merged-cascade"
        r["tokens_in"] = primary_in + secondary_in
        r["tokens_out"] = primary_out + secondary_out
        r["primary_passed"] = False
        r["secondary_used"] = True
        r["secondary_passed"] = s["pass"]
        merged.append(r)

    with args.out.open("w") as fh:
        for r in merged:
            fh.write(json.dumps(r) + "\n")

    n_pass = sum(1 for r in merged if r["pass"])
    total = sum((r.get("tokens_in") or 0) + (r.get("tokens_out") or 0) for r in merged)
    secondary_used = sum(1 for r in merged if r.get("secondary_used"))
    secondary_passed = sum(1 for r in merged if r.get("secondary_passed"))
    print(f"=== MERGED ===")
    print(f"pass:               {n_pass}/{len(merged)}")
    print(f"total tokens:       {total:,}")
    print(f"tok_per_pass:       {total/max(1,n_pass):.0f}")
    print(f"secondary used on:  {secondary_used} problems (of which {secondary_passed} passed there)")
    print(f"out -> {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
