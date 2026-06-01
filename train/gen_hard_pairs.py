#!/usr/bin/env python3
"""Validate and merge subagent-generated hard-problem AGC pairs.

Subagents write JSON files to /tmp/hard-pairs/<id>.json containing
{topic, objective, solution} where:
  - the OBJECTIVE specifies a function name and behaviour
  - the SOLUTION is a complete `(module ...)` with embedded tests
  - running `agc check` on the solution reports `(ok (tests-passed N/N))`

This script reads them all, runs each through `agc check`, drops the
ones that fail, and merges the survivors into the requested JSONL.

The output format matches `agc_pairs_claude.jsonl` so it can be
concatenated with the existing corpus.

Usage:
  python3 train/gen_hard_pairs.py --in-dir /tmp/hard-pairs \\
    --out train/dataset/agc_pairs_hard.jsonl
"""
from __future__ import annotations
import argparse
import json
import re
import subprocess
import sys
import tempfile
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
AGC_DLL = REPO / "Agentic.Cli" / "bin" / "Debug" / "net8.0" / "Agentic.Cli.dll"


def validate(solution: str) -> tuple[bool, int, int, str]:
    """Run `agc check` on a solution. Returns (ok, passed, total, tail)."""
    with tempfile.NamedTemporaryFile("w", suffix=".ag", delete=False, dir="/tmp") as f:
        f.write(solution)
        path = f.name
    try:
        proc = subprocess.run(
            ["dotnet", str(AGC_DLL), "check", path,
             "--allow-env", "--allow-file", "--allow-http", "--allow-db"],
            capture_output=True, text=True, timeout=30,
        )
        out = (proc.stdout + "\n" + proc.stderr)
        m = re.search(r"\(ok \(tests-passed (\d+)/(\d+)\)\)", out)
        if m and int(m.group(1)) == int(m.group(2)) and int(m.group(2)) >= 3:
            return True, int(m.group(1)), int(m.group(2)), ""
        return False, 0, 0, out[-400:]
    except Exception as e:
        return False, 0, 0, str(e)
    finally:
        Path(path).unlink(missing_ok=True)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--in-dir", type=Path, default=Path("/tmp/hard-pairs"))
    ap.add_argument("--out", type=Path, required=True)
    ap.add_argument("--source", default="subagent-generated-2026-05-29-hard")
    args = ap.parse_args()

    if not args.in_dir.exists():
        print(f"input dir not found: {args.in_dir}")
        return 1

    written, skipped, errors = 0, 0, []
    args.out.parent.mkdir(parents=True, exist_ok=True)
    with args.out.open("w") as out_f:
        for json_path in sorted(args.in_dir.glob("*.json")):
            try:
                rec = json.loads(json_path.read_text())
            except Exception as e:
                errors.append((json_path.name, f"json-decode: {e}"))
                skipped += 1
                continue
            sol = rec.get("solution", "").strip()
            if not sol:
                skipped += 1; errors.append((json_path.name, "empty solution")); continue
            ok, passed, total, why = validate(sol)
            if not ok:
                skipped += 1; errors.append((json_path.name, why[:200])); continue
            out = {
                "category": "hard-multistep",
                "topic": rec.get("topic") or json_path.stem,
                "objective": rec["objective"],
                "solution": sol,
                "tests_passed": passed,
                "source": args.source,
            }
            out_f.write(json.dumps(out) + "\n")
            written += 1

    print(f"written: {written}")
    print(f"skipped: {skipped}")
    if errors:
        print("first 10 errors:")
        for name, why in errors[:10]:
            print(f"  {name}: {why}")
    print(f"out -> {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
