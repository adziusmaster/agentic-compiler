#!/usr/bin/env python3
"""Validate and merge subagent-generated naming-discipline pairs.

Subagents write JSON files to /tmp/naming-pairs/<id>.json containing
{topic, objective, solution} where:
  - the OBJECTIVE specifies an exact function name to use
  - the SOLUTION's `(defun <name> ...)` matches that exact name
  - the SOLUTION runs cleanly through `agc check`

This script reads them all, runs each through `agc check`, drops the
ones that fail, and merges the survivors into a new pairs JSONL.
"""
from __future__ import annotations
import argparse, json, subprocess, sys, tempfile
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
AGC_DLL = REPO / "Agentic.Cli" / "bin" / "Debug" / "net8.0" / "Agentic.Cli.dll"


def validate(solution: str) -> tuple[bool, str]:
    with tempfile.NamedTemporaryFile("w", suffix=".ag", delete=False, dir="/tmp") as f:
        f.write(solution)
        path = f.name
    try:
        proc = subprocess.run(
            ["dotnet", str(AGC_DLL), "check", path,
             "--allow-env", "--allow-file", "--allow-http", "--allow-db"],
            capture_output=True, text=True, timeout=20,
        )
        out = (proc.stdout + "\n" + proc.stderr)
        # tests-passed N/N where N>0 indicates ok
        import re
        m = re.search(r"\(ok \(tests-passed (\d+)/(\d+)\)\)", out)
        if m and int(m.group(2)) > 0 and int(m.group(1)) == int(m.group(2)):
            return True, ""
        return False, out[-300:]
    except Exception as e:
        return False, str(e)
    finally:
        Path(path).unlink(missing_ok=True)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--in-dir", type=Path, default=Path("/tmp/naming-pairs"))
    ap.add_argument("--out", type=Path, required=True)
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
            ok, why = validate(sol)
            if not ok:
                skipped += 1; errors.append((json_path.name, why[:150])); continue
            out = {
                "category": "naming-discipline",
                "topic": rec.get("topic") or json_path.stem,
                "objective": rec["objective"],
                "solution": sol,
                "tests_passed": 0,  # will be set by validation below if needed
                "source": "subagent-generated-2026-05-07",
            }
            out_f.write(json.dumps(out) + "\n")
            written += 1

    print(f"written: {written}")
    print(f"skipped: {skipped}")
    if errors:
        print("first 5 errors:")
        for name, why in errors[:5]:
            print(f"  {name}: {why}")
    print(f"out -> {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
