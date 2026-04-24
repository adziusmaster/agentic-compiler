#!/usr/bin/env python3
"""Aggregate bench results across tracks into a paper-table summary.

Reads results/YYYY-MM-DD/{agc,python,python-oneshot}.jsonl and emits
per-track totals + per-category breakdowns.
"""
import json
import sys
from pathlib import Path
from statistics import mean, median


def load(path: Path) -> list[dict]:
    return [json.loads(line) for line in path.read_text().splitlines() if line.strip()]


def category_of(pid: str) -> str:
    n = int(pid.split("-", 1)[0])
    if n <= 10: return "pure"
    if n <= 20: return "cap"
    return "multi"


def summarize(rows: list[dict], label: str) -> None:
    n = len(rows)
    passed = sum(1 for r in rows if r["pass"])
    attempts = [r["attempts"] for r in rows]
    tok_in = [r["tokens_in"] for r in rows]
    tok_out = [r["tokens_out"] for r in rows]
    loc = [r["source_loc"] for r in rows]
    wall = [r["wall_time_s"] for r in rows]

    print(f"\n=== {label} ({n} problems) ===")
    print(f"  pass          : {passed}/{n} ({100*passed/n:.0f}%)")
    print(f"  attempts  mean: {mean(attempts):.2f}  median: {median(attempts):.1f}  max: {max(attempts)}")
    print(f"  tokens_in mean: {mean(tok_in):7.0f}  median: {median(tok_in):7.0f}  total: {sum(tok_in):7d}")
    print(f"  tokens_out mean: {mean(tok_out):6.0f}  median: {median(tok_out):7.0f}  total: {sum(tok_out):7d}")
    print(f"  source_loc mean: {mean(loc):6.1f}  median: {median(loc):6.1f}")
    print(f"  wall_time_s mean: {mean(wall):5.1f}s  median: {median(wall):5.1f}s")

    # per-category
    for cat in ("pure", "cap", "multi"):
        sub = [r for r in rows if category_of(r["id"]) == cat]
        if not sub: continue
        p = sum(1 for r in sub if r["pass"])
        print(f"  [{cat:5}] pass {p}/{len(sub)}  tok_in mean {mean(r['tokens_in'] for r in sub):6.0f}  loc mean {mean(r['source_loc'] for r in sub):5.1f}")


def main(date: str) -> int:
    base = Path(__file__).parent / "results" / date
    for track in ("agc", "python", "python-oneshot"):
        f = base / f"{track}.jsonl"
        if not f.exists():
            print(f"missing: {f}")
            continue
        summarize(load(f), track)

    # Comparative table
    print("\n=== Cross-track comparison ===")
    agc = {r["id"]: r for r in load(base / "agc.jsonl")}
    py = {r["id"]: r for r in load(base / "python.jsonl")}
    pyo = {r["id"]: r for r in load(base / "python-oneshot.jsonl")}

    agc_in = [r["tokens_in"] for r in agc.values()]
    py_in = [r["tokens_in"] for r in py.values()]
    pyo_in = [r["tokens_in"] for r in pyo.values()]

    print(f"  mean tokens_in:  agc={mean(agc_in):.0f}  python={mean(py_in):.0f}  oneshot={mean(pyo_in):.0f}")
    print(f"    → AGC uses {mean(agc_in)/mean(py_in):.1f}× input tokens vs python retry")

    # Pass-rate gaps
    def rate(m): return 100 * sum(1 for r in m.values() if r["pass"]) / len(m)
    print(f"  pass rate:       agc={rate(agc):.0f}%  python={rate(py):.0f}%  oneshot={rate(pyo):.0f}%")
    print(f"    → retry-oracle uplift (oneshot → python): +{rate(py)-rate(pyo):.0f} pts")
    print(f"    → verifier uplift (oneshot → agc):        {rate(agc)-rate(pyo):+.0f} pts")

    return 0


if __name__ == "__main__":
    date = sys.argv[1] if len(sys.argv) > 1 else "2026-04-24"
    sys.exit(main(date))
