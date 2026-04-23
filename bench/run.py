#!/usr/bin/env python3
"""AgenticCompiler D1 benchmark harness.

Runs the 30-problem suite across one of three tracks:
  agc              — `agc agent "<objective>"` with 5-attempt reflection loop
  python           — Python + pytest with 5-attempt retry oracle
  python-oneshot   — Python + pytest, single attempt (no retry)

Results land in results/YYYY-MM-DD/<track>.jsonl, one line per problem.

Usage:
  python3 run.py --track agc
  python3 run.py --track python --only 01,02,03
  python3 run.py --track python-oneshot --dry-run
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import re
import subprocess
import sys
import tempfile
import time
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Optional

BENCH_DIR = Path(__file__).resolve().parent
PROBLEMS_DIR = BENCH_DIR / "problems"
RESULTS_DIR = BENCH_DIR / "results"
REPO_ROOT = BENCH_DIR.parent
AGC_PROJECT = REPO_ROOT / "Agentic.Cli"

MAX_ATTEMPTS_RETRY = 5
MAX_ATTEMPTS_ONESHOT = 1
WALL_TIMEOUT_S = 120


@dataclass
class Result:
    id: str
    track: str
    pass_: bool = field(metadata={"json_key": "pass"})
    attempts: int = 0
    wall_time_s: float = 0.0
    tokens_in: int = 0
    tokens_out: int = 0
    source_loc: int = 0
    capabilities: list = field(default_factory=list)
    decomposition_depth: int = 0
    error_category: Optional[str] = None
    error_detail: Optional[str] = None

    def to_jsonl(self) -> str:
        d = asdict(self)
        d["pass"] = d.pop("pass_")
        return json.dumps(d, separators=(",", ":"))


def discover_problems(only: Optional[set[str]] = None) -> list[Path]:
    probs = sorted(p for p in PROBLEMS_DIR.iterdir() if p.is_dir())
    if only:
        probs = [p for p in probs if any(p.name.startswith(tag) for tag in only)]
    return probs


def load_meta(prob: Path) -> dict:
    meta_path = prob / "meta.json"
    if not meta_path.exists():
        return {"capabilities": [], "permissions": [], "mocks": {}}
    return json.loads(meta_path.read_text())


def read_text(path: Path) -> str:
    return path.read_text() if path.exists() else ""


def count_loc(source: str) -> int:
    return sum(1 for line in source.splitlines() if line.strip())


def count_helpers_ag(source: str) -> int:
    return len(re.findall(r"\(defun\s+", source))


def count_helpers_py(source: str) -> int:
    return len(re.findall(r"^def\s+\w+", source, re.MULTILINE))


def extract_capabilities_ag(source: str) -> list[str]:
    return re.findall(r'@capability\s+"([^"]+)"', source)


# ---------------------------------------------------------------------
# AGC track
# ---------------------------------------------------------------------

def run_agc(prob: Path, meta: dict) -> Result:
    objective = read_text(prob / "objective.md").strip()
    tests_ag = read_text(prob / "tests.ag").strip()
    perm_flags = [f"--allow-{p}" for p in meta.get("permissions", [])]

    prompt = (
        f"{objective}\n\n"
        f"Your module MUST satisfy these tests exactly (copy them into the module):\n\n"
        f"```\n{tests_ag}\n```\n"
    )

    t0 = time.monotonic()
    with tempfile.TemporaryDirectory() as td:
        cmd = [
            "dotnet", "run", "--project", str(AGC_PROJECT), "--",
            "agent", prompt, "--out", prob.name.replace("-", "_"),
            "--json",
        ] + perm_flags
        try:
            proc = subprocess.run(
                cmd, cwd=td, capture_output=True, text=True,
                timeout=WALL_TIMEOUT_S,
            )
        except subprocess.TimeoutExpired:
            return Result(prob.name, "agc", False, error_category="timeout",
                          wall_time_s=WALL_TIMEOUT_S)

        wall = time.monotonic() - t0
        stdout = proc.stdout + "\n" + proc.stderr

        attempts = max(1, len(re.findall(r"\[ATTEMPT \d+\]", stdout)))
        tok_in = sum(int(m) for m in re.findall(r"tokens-in[:= ]+(\d+)", stdout, re.I))
        tok_out = sum(int(m) for m in re.findall(r"tokens-out[:= ]+(\d+)", stdout, re.I))
        passed = bool(re.search(r"\[SUCCESS\]", stdout)) and proc.returncode == 0

        src_file = next(Path(td).glob("*.ag"), None)
        source = src_file.read_text() if src_file else ""
        loc = count_loc(source)
        caps = extract_capabilities_ag(source)
        depth = count_helpers_ag(source)

        err = None
        detail = None
        if not passed:
            err = classify_agc_error(stdout)
            detail = stdout[-800:]

        return Result(
            id=prob.name, track="agc", pass_=passed,
            attempts=attempts, wall_time_s=wall,
            tokens_in=tok_in, tokens_out=tok_out,
            source_loc=loc, capabilities=caps, decomposition_depth=depth,
            error_category=err, error_detail=detail,
        )


def classify_agc_error(stdout: str) -> str:
    if "[FATAL]" in stdout and "compile" in stdout.lower():
        return "compile-error"
    if "assert-eq" in stdout or "test-fail" in stdout:
        return "test-fail"
    if "budget" in stdout.lower():
        return "budget-exceeded"
    return "unknown"


# ---------------------------------------------------------------------
# Python tracks
# ---------------------------------------------------------------------

def run_python(prob: Path, meta: dict, *, oneshot: bool) -> Result:
    objective = read_text(prob / "objective.md").strip()
    tests_py = read_text(prob / "tests.py").strip()
    track = "python-oneshot" if oneshot else "python"
    max_attempts = MAX_ATTEMPTS_ONESHOT if oneshot else MAX_ATTEMPTS_RETRY

    system_prompt = (
        "You are a careful Python engineer. Given a problem and a pytest "
        "test file, write a single `solution.py` module that makes the "
        "tests pass. Output ONLY the Python source, no markdown fences, "
        "no commentary."
    )
    user_prompt = (
        f"{objective}\n\n"
        f"Tests (pytest, in `test_solution.py`, imports from `solution`):\n\n"
        f"```python\n{tests_py}\n```\n\n"
        f"Write `solution.py`."
    )

    client = LLMClient.auto()
    if client is None:
        return Result(prob.name, track, False,
                      error_category="no-provider",
                      error_detail="No ANTHROPIC_API_KEY/OPENAI_API_KEY/GEMINI_API_KEY set")

    t0 = time.monotonic()
    tok_in = 0
    tok_out = 0
    source = ""
    last_err = ""

    with tempfile.TemporaryDirectory() as td:
        td_path = Path(td)
        (td_path / "test_solution.py").write_text(tests_py)

        messages = [{"role": "user", "content": user_prompt}]
        for attempt in range(1, max_attempts + 1):
            try:
                reply, ti, to = client.complete(system_prompt, messages)
            except Exception as e:
                return Result(prob.name, track, False,
                              attempts=attempt,
                              wall_time_s=time.monotonic() - t0,
                              error_category="llm-error",
                              error_detail=str(e))
            tok_in += ti
            tok_out += to
            source = extract_python_source(reply)
            (td_path / "solution.py").write_text(source)

            verdict = subprocess.run(
                [sys.executable, "-m", "pytest", "test_solution.py", "-q",
                 "--tb=short", "-rN"],
                cwd=td_path, capture_output=True, text=True,
                timeout=WALL_TIMEOUT_S,
            )
            passed = verdict.returncode == 0
            last_err = (verdict.stdout + verdict.stderr)[-2000:]
            if passed:
                break
            if attempt == max_attempts:
                break
            messages.append({"role": "assistant", "content": reply})
            messages.append({"role": "user",
                             "content": f"Tests failed. pytest output:\n\n{last_err}\n\n"
                                        f"Rewrite `solution.py` in full."})

    wall = time.monotonic() - t0
    return Result(
        id=prob.name, track=track, pass_=passed,
        attempts=attempt, wall_time_s=wall,
        tokens_in=tok_in, tokens_out=tok_out,
        source_loc=count_loc(source),
        capabilities=[], decomposition_depth=count_helpers_py(source),
        error_category=None if passed else "test-fail",
        error_detail=None if passed else last_err,
    )


def extract_python_source(reply: str) -> str:
    m = re.search(r"```(?:python)?\s*\n(.*?)```", reply, re.DOTALL)
    return (m.group(1) if m else reply).strip() + "\n"


# ---------------------------------------------------------------------
# LLM client (Python tracks)
# ---------------------------------------------------------------------

class LLMClient:
    @staticmethod
    def auto() -> Optional["LLMClient"]:
        provider = os.environ.get("AGENTIC_PROVIDER", "").lower()
        if provider == "anthropic" or os.environ.get("ANTHROPIC_API_KEY"):
            return AnthropicClient(
                os.environ["ANTHROPIC_API_KEY"],
                os.environ.get("ANTHROPIC_MODEL", "claude-sonnet-4-6"),
            )
        if provider == "openai" or os.environ.get("OPENAI_API_KEY"):
            return OpenAIClient(
                os.environ["OPENAI_API_KEY"],
                os.environ.get("OPENAI_MODEL", "gpt-4o"),
            )
        if provider == "gemini" or os.environ.get("GEMINI_API_KEY"):
            return GeminiClient(
                os.environ["GEMINI_API_KEY"],
                os.environ.get("GEMINI_MODEL", "gemini-2.5-flash"),
            )
        return None

    def complete(self, system: str, messages: list) -> tuple[str, int, int]:
        raise NotImplementedError


class AnthropicClient(LLMClient):
    def __init__(self, key: str, model: str):
        self.key = key
        self.model = model

    def complete(self, system, messages):
        import urllib.request
        body = json.dumps({
            "model": self.model,
            "max_tokens": 4096,
            "system": system,
            "messages": messages,
        }).encode()
        req = urllib.request.Request(
            "https://api.anthropic.com/v1/messages",
            data=body,
            headers={
                "x-api-key": self.key,
                "anthropic-version": "2023-06-01",
                "content-type": "application/json",
            },
        )
        with urllib.request.urlopen(req, timeout=60) as resp:
            data = json.loads(resp.read())
        text = "".join(b.get("text", "") for b in data.get("content", []))
        usage = data.get("usage", {})
        return text, usage.get("input_tokens", 0), usage.get("output_tokens", 0)


class OpenAIClient(LLMClient):
    def __init__(self, key: str, model: str):
        self.key = key
        self.model = model

    def complete(self, system, messages):
        import urllib.request
        msgs = [{"role": "system", "content": system}] + messages
        body = json.dumps({
            "model": self.model,
            "messages": msgs,
        }).encode()
        req = urllib.request.Request(
            "https://api.openai.com/v1/chat/completions",
            data=body,
            headers={
                "authorization": f"Bearer {self.key}",
                "content-type": "application/json",
            },
        )
        with urllib.request.urlopen(req, timeout=60) as resp:
            data = json.loads(resp.read())
        text = data["choices"][0]["message"]["content"]
        usage = data.get("usage", {})
        return text, usage.get("prompt_tokens", 0), usage.get("completion_tokens", 0)


class GeminiClient(LLMClient):
    def __init__(self, key: str, model: str):
        self.key = key
        self.model = model

    def complete(self, system, messages):
        import urllib.request
        contents = [
            {"role": "user" if m["role"] == "user" else "model",
             "parts": [{"text": m["content"]}]}
            for m in messages
        ]
        body = json.dumps({
            "systemInstruction": {"parts": [{"text": system}]},
            "contents": contents,
            "generationConfig": {"maxOutputTokens": 4096},
        }).encode()
        url = (f"https://generativelanguage.googleapis.com/v1beta/models/"
               f"{self.model}:generateContent?key={self.key}")
        req = urllib.request.Request(
            url,
            data=body,
            headers={"content-type": "application/json"},
        )
        with urllib.request.urlopen(req, timeout=60) as resp:
            data = json.loads(resp.read())
        cands = data.get("candidates", [])
        text = ""
        if cands:
            parts = cands[0].get("content", {}).get("parts", [])
            text = "".join(p.get("text", "") for p in parts)
        usage = data.get("usageMetadata", {})
        return text, usage.get("promptTokenCount", 0), usage.get("candidatesTokenCount", 0)


# ---------------------------------------------------------------------
# Driver
# ---------------------------------------------------------------------

def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--track", required=True,
                    choices=["agc", "python", "python-oneshot"])
    ap.add_argument("--only", help="comma-separated problem prefixes (e.g. 01,02)")
    ap.add_argument("--dry-run", action="store_true",
                    help="validate problem files only, no LLM calls")
    ap.add_argument("--out", help="override results jsonl path")
    args = ap.parse_args()

    only = set(args.only.split(",")) if args.only else None
    problems = discover_problems(only)
    if not problems:
        print(f"no problems matched selector: {args.only}", file=sys.stderr)
        return 2

    if args.dry_run:
        for p in problems:
            meta = load_meta(p)
            ok = (p / "objective.md").exists() \
                 and (p / "tests.ag").exists() \
                 and (p / "tests.py").exists()
            print(f"{p.name:30} meta-caps={meta.get('capabilities', []):} files-ok={ok}")
        return 0

    today = dt.date.today().isoformat()
    out_dir = RESULTS_DIR / today
    out_dir.mkdir(parents=True, exist_ok=True)
    out_path = Path(args.out) if args.out else out_dir / f"{args.track}.jsonl"

    with out_path.open("a") as out_f:
        for p in problems:
            meta = load_meta(p)
            t = time.monotonic()
            if args.track == "agc":
                r = run_agc(p, meta)
            elif args.track == "python":
                r = run_python(p, meta, oneshot=False)
            else:
                r = run_python(p, meta, oneshot=True)
            status = "PASS" if r.pass_ else "FAIL"
            print(f"[{status}] {p.name:30} attempts={r.attempts} "
                  f"wall={r.wall_time_s:.1f}s loc={r.source_loc} "
                  f"err={r.error_category or '-'}")
            out_f.write(r.to_jsonl() + "\n")
            out_f.flush()

    print(f"\nResults: {out_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
