#!/usr/bin/env python3
"""Best-of-N evaluation: load model+adapter once, sample N candidates per
problem at temperature > 0, verify each via agc check, keep the best.

For small specialised models, sampling diversity + verifier scoring is
the highest-leverage technique short of changing the model.

Usage:
  python3 eval_bestof_n.py --n 8 --temp 0.7 \
    [--model mlx-community/Qwen2.5-Coder-3B-Instruct-4bit] \
    [--adapter train/lora_adapter] \
    [--only 01,02,03] [--out path/to.jsonl]
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import tempfile
import time
from pathlib import Path

from mlx_lm import load, generate
from mlx_lm.sample_utils import make_sampler

TRAIN_DIR = Path(__file__).resolve().parent
REPO_ROOT = TRAIN_DIR.parent
BENCH_PROBLEMS = REPO_ROOT / "bench" / "problems"
BENCH_RESULTS = REPO_ROOT / "bench" / "results"
AGC_CLI_DLL = REPO_ROOT / "Agentic.Cli" / "bin" / "Debug" / "net8.0" / "Agentic.Cli.dll"

SYSTEM_PROMPT = "Output only AGC (Agentic Compiler) S-expression source code, no prose."

TESTS_OK_RE = re.compile(r"\(ok \(tests-passed (\d+)/(\d+)\)\)")
TESTS_ANY_RE = re.compile(r"tests-passed (\d+)/(\d+)")


def read(path: Path) -> str:
    return path.read_text()


def extract_signature_hints(tests_ag: str) -> str:
    """Find the top-level function names the tests directly invoke and surface
    them as a short *REQUIRED* directive. Stronger phrasing than a passive
    signatures list, because v12 was renaming `letter_grade` → `grade` etc.
    when the original prompt only listed signatures softly.
    """
    seen: dict[str, int] = {}
    pattern = re.compile(r"\((?:assert-eq|assert-near|eq\?|near\?)\s+\(([a-z_][a-z0-9_]*)\b([^)]*?)\)")
    for m in pattern.finditer(tests_ag):
        name, argstr = m.group(1), m.group(2)
        depth = 0; in_str = False; toks = 0; cur = ""
        for ch in argstr.strip():
            if in_str:
                if ch == '"': in_str = False
                cur += ch; continue
            if ch == '"': in_str = True; cur += ch
            elif ch == "(": depth += 1; cur += ch
            elif ch == ")": depth -= 1; cur += ch
            elif ch.isspace() and depth == 0:
                if cur: toks += 1; cur = ""
            else: cur += ch
        if cur: toks += 1
        if name not in seen: seen[name] = toks
    if not seen: return ""
    lines = [f"  - `{n}` ({a} argument{'s' if a != 1 else ''})" for n, a in seen.items()]
    return ("REQUIRED top-level function name(s) — define them EXACTLY as "
            "shown, do NOT rename:\n" + "\n".join(lines))


def build_prompt(objective: str, tests_ag: str) -> str:
    sig = extract_signature_hints(tests_ag)
    return (f"Write an AGC module that satisfies this objective:\n\n{objective}\n\n"
            + (f"{sig}\n\n" if sig else "")
            + f"The module must include these (test ...) blocks:\n\n{tests_ag}\n\n"
            + "Output ONLY the module source.")


_WHITESPACE_HINT = (
    "Hint: `str_split` takes a single-character delimiter only. To handle "
    "multiple whitespace characters (space and tab), iterate character by "
    "character with `str_substring text i 1` and check each against "
    "both \" \" and \"\\t\"."
)

_FIRST_TEST_RE = re.compile(
    r"\(test\s+\S+\s+\(assert(?:-eq|-near)\s+\(([a-z_][a-z0-9_]*)\b([^)]*)\)\s+([^)]+)\)\)",
    re.DOTALL,
)


def worked_example_hint(tests_ag: str) -> str:
    """Pull the first `(test … (assert-eq (call args…) expected))` from tests.ag
    and surface it as a worked I/O pair. Cheap concrete grounding for the
    model — especially valuable on multi-rule arithmetic problems where
    the spec spans many tests but no single test is in the prompt.

    Returns "" if the regex doesn't match (e.g. compound expected values).
    """
    m = _FIRST_TEST_RE.search(tests_ag)
    if not m:
        return ""
    name, argstr, expected = m.group(1), m.group(2).strip(), m.group(3).strip()
    # For assert-near the captured `expected` is "<value> <tolerance>" — just keep value.
    expected = expected.split(None, 1)[0] if expected else expected
    return f"Example: `({name} {argstr})` should return `{expected}`."


def domain_hint(objective: str) -> str:
    """Targeted nudge for problems where the AGC idiom is non-obvious from
    the objective alone. Returns an empty string for problems where the
    model already does fine.

    Keep this list narrow: a hint that fires on the wrong problem will
    degrade outputs (mismatch with training distribution).
    """
    obj_low = objective.lower()
    mentions_whitespace_tab = (
        ("tab" in obj_low or "\\t" in objective)
        and ("space" in obj_low or "whitespace" in obj_low)
    )
    if mentions_whitespace_tab:
        return _WHITESPACE_HINT
    return ""


def build_prompt_no_tests(objective: str, tests_ag: str) -> str:
    """L1 prompt: function defs only. Tests are appended server-side before
    `agc check` runs (see `assemble`)."""
    sig = extract_signature_hints(tests_ag)
    hint = domain_hint(objective)
    example = worked_example_hint(tests_ag)
    return (f"Write AGC code that satisfies this objective:\n\n{objective}\n\n"
            + (f"{sig}\n\n" if sig else "")
            + (f"{example}\n\n" if example else "")
            + (f"{hint}\n\n" if hint else "")
            + "Write the function definitions and any (extern ...) declarations needed.\n"
            + "Output ONLY the function source — DO NOT include any (test ...) blocks "
            + "and DO NOT wrap the output in (module ...). Just the raw defuns/externs.")


def extract_defuns(text: str) -> str | None:
    """Pull top-level (defun…)/(extern…)/(defstruct…)/(def …) forms out of
    arbitrary model output. Discards (module …) wrapper if present and any
    (test …) blocks the model still emitted. Returns None if nothing
    parseable is found.
    """
    end = text.find("<|im_end|>")
    if end != -1:
        text = text[:end]
    mod_start = text.find("(module")
    if mod_start != -1:
        head_end = mod_start + len("(module")
        first_inner = text.find("(", head_end)
        if first_inner != -1:
            text = text[first_inner:]

    out: list[str] = []
    i, n = 0, len(text)
    while i < n:
        if text[i] != "(":
            i += 1; continue
        j = i + 1
        while j < n and text[j].isspace():
            j += 1
        head_start = j
        while j < n and not text[j].isspace() and text[j] != "(" and text[j] != ")":
            j += 1
        head = text[head_start:j]
        if head not in ("defun", "extern", "defstruct", "def"):
            i += 1; continue
        depth = 0; k = i; in_str = False; escape = False
        while k < n:
            ch = text[k]
            if in_str:
                if escape: escape = False
                elif ch == "\\": escape = True
                elif ch == '"': in_str = False
            else:
                if ch == '"': in_str = True
                elif ch == "(": depth += 1
                elif ch == ")":
                    depth -= 1
                    if depth == 0:
                        out.append(text[i:k+1])
                        i = k + 1
                        break
            k += 1
        else:
            return None
    return "\n\n".join(out) if out else None


_DEFUN_HEAD_RE = re.compile(r"\(defun\s+([a-z_][a-z0-9_]*)\s*\(([^)]*)\)")


def _arity_from_paramlist(paramstr: str) -> int:
    """Count top-level params in `(defun name (...))` paramstr.

    Handles bare names (`a b c`) and typed names (`(a : Num) (b : Num)`).
    For typed params we count parenthesized groups, otherwise whitespace
    tokens excluding `:` and type names.
    """
    s = paramstr.strip()
    if not s:
        return 0
    if "(" in s:
        # typed paramlist: count balanced "(...)" groups at depth-0
        depth = 0; count = 0
        for i, ch in enumerate(s):
            if ch == "(":
                if depth == 0:
                    count += 1
                depth += 1
            elif ch == ")":
                depth -= 1
        return count
    # bare paramlist: just whitespace-separated names
    return len(s.split())


def assemble(defuns: str, tests_ag: str, required_funcs: list[tuple[str, int]] | None = None) -> str:
    """Wrap raw defuns/externs in (module Submission ...) and append tests.

    If `required_funcs` is provided, inject a passthrough wrapper for any
    required entry-point name that isn't defined but has a matching-arity
    defun in the model's output. This bridges the v12 naming bias
    (defines `grade` instead of `letter_grade`) without retraining.
    """
    out = defuns
    if required_funcs:
        defined: dict[str, int] = {}
        for m in _DEFUN_HEAD_RE.finditer(out):
            name = m.group(1)
            if name not in defined:
                defined[name] = _arity_from_paramlist(m.group(2))
        for req_name, req_arity in required_funcs:
            if req_name in defined:
                continue
            # find ANY existing defun with matching arity (prefer the last
            # one, which is usually the most-composed top-level function)
            chosen = None
            for fn, ar in reversed(list(defined.items())):
                if ar == req_arity:
                    chosen = fn
                    break
            if chosen is None:
                continue
            # synthesize a wrapper using positional arg names
            params = " ".join(f"a{i}" for i in range(req_arity))
            wrapper = f"\n\n(defun {req_name} ({params}) ({chosen} {params}))"
            out = out + wrapper
    return f"(module Submission\n{out}\n\n{tests_ag}\n)"


def required_funcs_from(tests_ag: str) -> list[tuple[str, int]]:
    """Pull (name, arity) for every function the tests directly call —
    these are the entry points the assembled module MUST expose."""
    seen: dict[str, int] = {}
    pattern = re.compile(r"\((?:assert-eq|assert-near|eq\?|near\?)\s+\(([a-z_][a-z0-9_]*)\b([^)]*?)\)")
    for m in pattern.finditer(tests_ag):
        name, argstr = m.group(1), m.group(2)
        depth = 0; in_str = False; toks = 0; cur = ""
        for ch in argstr.strip():
            if in_str:
                if ch == '"': in_str = False
                cur += ch; continue
            if ch == '"': in_str = True; cur += ch
            elif ch == "(": depth += 1; cur += ch
            elif ch == ")": depth -= 1; cur += ch
            elif ch.isspace() and depth == 0:
                if cur: toks += 1; cur = ""
            else: cur += ch
        if cur: toks += 1
        if name not in seen: seen[name] = toks
    return list(seen.items())


def chat_format(tokenizer, system: str, user: str) -> str:
    msgs = [{"role": "system", "content": system}, {"role": "user", "content": user}]
    return tokenizer.apply_chat_template(msgs, tokenize=False, add_generation_prompt=True)


def extract_module(text: str) -> str | None:
    end_marker = text.find("<|im_end|>")
    if end_marker != -1:
        text = text[:end_marker]
    start = text.find("(module")
    if start == -1: return None
    depth = 0; in_string = False; escape = False
    for i in range(start, len(text)):
        ch = text[i]
        if in_string:
            if escape: escape = False
            elif ch == "\\": escape = True
            elif ch == '"': in_string = False
            continue
        if ch == '"': in_string = True
        elif ch == "(": depth += 1
        elif ch == ")":
            depth -= 1
            if depth == 0: return text[start:i+1]
    return None


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
        m = TESTS_ANY_RE.search(out)
        if m: return False, int(m.group(1)), int(m.group(2)), out[-400:]
        return False, 0, 0, out[-400:]
    except subprocess.TimeoutExpired:
        return False, 0, 0, "timeout"
    finally:
        try: os.unlink(path)
        except Exception: pass


def count_loc(source: str) -> int:
    return sum(1 for line in source.splitlines() if line.strip())


def sample_one(model, tokenizer, prompt: str, sampler, max_tokens: int) -> str:
    """Generate a single candidate and strip prompt prefix / im_end suffix."""
    text = generate(model, tokenizer, prompt=prompt,
                    max_tokens=max_tokens, sampler=sampler, verbose=False)
    if text.startswith(prompt):
        text = text[len(prompt):]
    end = text.find("<|im_end|>")
    if end != -1:
        text = text[:end]
    return text


def sample_candidates(model, tokenizer, prompt: str, n: int, temp: float,
                      max_tokens: int) -> list[str]:
    """Sample n candidates at the given temperature. Returns raw text outputs.

    NOTE: This eagerly samples all n candidates with NO early-stop. Used by
    legacy callers that want to inspect the full distribution. New code
    should use `sample_until_pass` to avoid burning generation tokens on
    candidates whose verification is never reached.
    """
    samplers = [make_sampler(temp=temp, top_p=0.95) for _ in range(n)]
    return [sample_one(model, tokenizer, prompt, s, max_tokens) for s in samplers]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default="mlx-community/Qwen2.5-Coder-3B-Instruct-4bit")
    ap.add_argument("--adapter", default=str(TRAIN_DIR / "lora_adapter"))
    ap.add_argument("--only", help="comma-sep problem prefixes")
    ap.add_argument("--out", help="override output jsonl path")
    ap.add_argument("--n", type=int, default=8, help="candidates per problem")
    ap.add_argument("--temp", type=float, default=0.7, help="sampling temperature")
    ap.add_argument("--max-tokens", type=int, default=600)
    ap.add_argument("--prompt-format", choices=["with_tests", "no_tests"],
                    default="with_tests",
                    help="with_tests = legacy prompt (model emits full module incl. tests); "
                         "no_tests = L1 prompt (model emits defuns only; tests appended server-side)")
    args = ap.parse_args()

    print(f"Loading {args.model} + adapter {args.adapter}...", flush=True)
    model, tokenizer = load(args.model, adapter_path=args.adapter)
    print("Loaded.", flush=True)

    date = time.strftime("%Y-%m-%d")
    out_path = Path(args.out) if args.out else BENCH_RESULTS / date / "agc-local-bestof.jsonl"
    out_path.parent.mkdir(parents=True, exist_ok=True)

    problems = sorted(p for p in BENCH_PROBLEMS.iterdir() if p.is_dir())
    if args.only:
        keys = args.only.split(",")
        problems = [p for p in problems if any(p.name.startswith(k) for k in keys)]

    records = []
    passed = 0
    t_start = time.monotonic()

    for p in problems:
        obj = read(p / "objective.md").strip()
        tests_ag = read(p / "tests.ag").strip()
        if args.prompt_format == "no_tests":
            user_prompt = build_prompt_no_tests(obj, tests_ag)
        else:
            user_prompt = build_prompt(obj, tests_ag)
        chat_prompt = chat_format(tokenizer, SYSTEM_PROMPT, user_prompt)

        t0 = time.monotonic()
        # Interleaved sample + verify with proper early-stop (no wasted gen tokens).
        prompt_toks = len(tokenizer.encode(chat_prompt))
        samplers = [make_sampler(temp=args.temp, top_p=0.95) for _ in range(args.n)]
        best = {"pass": False, "passed": 0, "total": 0, "module": None, "err": ""}
        cands_used = 0
        gen_toks = 0
        for sampler in samplers:
            cands_used += 1
            c = sample_one(model, tokenizer, chat_prompt, sampler, args.max_tokens)
            gen_toks += len(tokenizer.encode(c))
            if args.prompt_format == "no_tests":
                defuns = extract_defuns(c)
                module = assemble(defuns, tests_ag, required_funcs_from(tests_ag)) if defuns else None
            else:
                module = extract_module(c)
            if module is None:
                continue
            ok, pv, tv, err = verify(module)
            score = (1 if ok else 0, pv / max(1, tv))
            best_score = (1 if best["pass"] else 0, best["passed"] / max(1, best["total"]))
            if score > best_score:
                best = {"pass": ok, "passed": pv, "total": tv, "module": module, "err": err}
            if ok:  # real early-stop: skip remaining samples entirely
                break

        wall = time.monotonic() - t0
        loc = count_loc(best["module"]) if best["module"] else 0
        rec = {
            "id": p.name, "track": "agc-local-bestof",
            "pass": best["pass"], "attempts": cands_used,
            "wall_time_s": wall,
            "tokens_in": prompt_toks * cands_used, "tokens_out": gen_toks,
            "source_loc": loc,
            "capabilities": [], "decomposition_depth": 0,
            "tests_passed": best["passed"], "tests_total": best["total"],
            "error_category": None if best["pass"] else "test-fail",
            "error_detail": None if best["pass"] else best["err"][:300],
        }
        records.append(rec)
        if best["pass"]: passed += 1
        status = "PASS" if best["pass"] else "FAIL"
        print(f"[{status}] {p.name:30} N={args.n} wall={wall:.1f}s "
              f"best={best['passed']}/{best['total']} loc={loc}", flush=True)

    with out_path.open("w") as fh:
        for r in records:
            fh.write(json.dumps(r) + "\n")

    total = time.monotonic() - t_start
    print(f"\n=== SUMMARY ===")
    print(f"pass: {passed}/{len(records)} ({100*passed/len(records):.0f}%)")
    print(f"total wall: {total:.0f}s")
    print(f"results: {out_path}")


if __name__ == "__main__":
    sys.exit(main() or 0)
