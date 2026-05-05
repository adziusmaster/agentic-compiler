#!/usr/bin/env python3
"""Verify a JSONL batch of (objective, solution) pairs against agc check;
append passers to the main dataset jsonl. Fast; no LLM calls.

Usage:
  python3 verify_batch.py <stage.jsonl> [--out <main.jsonl>]
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
AGC_CLI_DLL = REPO_ROOT / "Agentic.Cli" / "bin" / "Debug" / "net8.0" / "Agentic.Cli.dll"
TESTS_OK_RE = re.compile(r"\(ok \(tests-passed (\d+)/(\d+)\)\)")


def verify(source: str) -> tuple[bool, int, int, str]:
    with tempfile.NamedTemporaryFile("w", suffix=".ag", delete=False, dir="/tmp") as f:
        f.write(source); path = f.name
    cmd = ["dotnet", str(AGC_CLI_DLL), "check", path,
           "--allow-env", "--allow-file", "--allow-http", "--allow-db"]
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True, timeout=20)
        out = proc.stdout + "\n" + proc.stderr
        m = TESTS_OK_RE.search(out)
        if m and int(m.group(2)) > 0:
            return True, int(m.group(1)), int(m.group(2)), ""
        return False, 0, 0, out[-300:]
    except subprocess.TimeoutExpired:
        return False, 0, 0, "timeout"
    finally:
        try: os.unlink(path)
        except Exception: pass


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("stage", help="JSONL of {category, topic, objective, solution}")
    ap.add_argument("--out", default=str(REPO_ROOT / "train" / "dataset" / "agc_pairs_claude.jsonl"))
    args = ap.parse_args()

    out_path = Path(args.out)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    seen = set()
    if out_path.exists():
        for line in out_path.open():
            seen.add(json.loads(line)["solution"])
    starting = len(seen)

    rows = [json.loads(l) for l in Path(args.stage).read_text().splitlines() if l.strip()]
    kept = 0
    failed = 0
    for r in rows:
        sol = r["solution"]
        if sol in seen:
            continue
        ok, p, t, err = verify(sol)
        if ok:
            r["tests_passed"] = p
            r["source"] = "claude"
            with out_path.open("a") as fh:
                fh.write(json.dumps(r) + "\n")
            seen.add(sol); kept += 1
        else:
            failed += 1
            print(f"FAIL [{r.get('topic','')[:40]:40}] {err[:120]}", flush=True)
    total_now = starting + kept
    print(f"\nKept {kept} / {len(rows)} (failed {failed}); total in {out_path.name}: {total_now}")


if __name__ == "__main__":
    sys.exit(main() or 0)
