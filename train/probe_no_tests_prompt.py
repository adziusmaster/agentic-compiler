#!/usr/bin/env python3
"""L1 probe: does an existing adapter follow a 'no test blocks' prompt
without retraining?

For each probed problem:
  - build the new prompt (asks for defuns/externs only, NO test blocks)
  - generate at temp=0
  - record whether output contains '(test' (instruction violation)
  - extract top-level (defun ...)/(extern ...)/(defstruct ...)
  - assemble: (module Submission <defuns> <tests.ag>)
  - run agc check
  - report pass/fail + token deltas

If most problems verify under the new prompt → plumb it into eval_bestof_n
without retraining. Otherwise → retrain.
"""
from __future__ import annotations
import argparse, json, os, re, subprocess, sys, tempfile, time
from pathlib import Path

from mlx_lm import load, generate
from mlx_lm.sample_utils import make_sampler

TRAIN_DIR = Path(__file__).resolve().parent
REPO_ROOT = TRAIN_DIR.parent
BENCH_PROBLEMS = REPO_ROOT / "bench" / "problems"
AGC_CLI_DLL = REPO_ROOT / "Agentic.Cli" / "bin" / "Debug" / "net8.0" / "Agentic.Cli.dll"

SYSTEM_PROMPT = "Output only AGC (Agentic Compiler) S-expression source code, no prose."

TESTS_OK_RE = re.compile(r"\(ok \(tests-passed (\d+)/(\d+)\)\)")
TESTS_ANY_RE = re.compile(r"tests-passed (\d+)/(\d+)")

# Same signature-hint extraction used by eval_bestof_n.py — keeps the
# only-instruction-changed contract clean.
def extract_signature_hints(tests_ag: str) -> str:
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
    lines = [f"  - `{n}` takes {a} argument(s)" for n, a in seen.items()]
    return "Function signatures (from the test calls):\n" + "\n".join(lines)


def build_prompt_no_tests(objective: str, tests_ag: str) -> str:
    """L1 prompt: function defs only, no test echo."""
    sig = extract_signature_hints(tests_ag)
    return (f"Write AGC code that satisfies this objective:\n\n{objective}\n\n"
            + (f"{sig}\n\n" if sig else "")
            + "Write the function definitions and any (extern ...) declarations needed.\n"
            + "Output ONLY the function source — DO NOT include any (test ...) blocks "
            + "and DO NOT wrap the output in (module ...). Just the raw defuns/externs.")


def chat_format(tokenizer, system: str, user: str) -> str:
    msgs = [{"role": "system", "content": system}, {"role": "user", "content": user}]
    return tokenizer.apply_chat_template(msgs, tokenize=False, add_generation_prompt=True)


def strip_im_end(text: str) -> str:
    end = text.find("<|im_end|>")
    return text[:end] if end != -1 else text


def extract_defuns(text: str) -> str | None:
    """Pull out top-level (defun…)/(extern…)/(defstruct…)/(def …) forms.

    Tolerant of stray prose around or between the forms. Returns None
    if nothing parseable is found.
    """
    text = strip_im_end(text)
    # If the model wrapped in (module …), unwrap once so we still get the inner forms.
    mod_start = text.find("(module")
    if mod_start != -1:
        # Skip "(module Name" header — find first nested "("
        head_end = mod_start + len("(module")
        first_inner = text.find("(", head_end)
        if first_inner != -1:
            text = text[first_inner:]

    out = []
    i = 0
    n = len(text)
    while i < n:
        if text[i] != "(":
            i += 1; continue
        # peek head token
        j = i + 1
        while j < n and text[j].isspace():
            j += 1
        head_start = j
        while j < n and not text[j].isspace() and text[j] != "(" and text[j] != ")":
            j += 1
        head = text[head_start:j]
        if head not in ("defun", "extern", "defstruct", "def"):
            i += 1; continue
        # scan balanced parens from i
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
            return None  # unbalanced
    return "\n\n".join(out) if out else None


def assemble(defuns: str, tests_ag: str) -> str:
    return f"(module Submission\n{defuns}\n\n{tests_ag}\n)"


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


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default="mlx-community/Qwen2.5-Coder-3B-Instruct-4bit")
    ap.add_argument("--adapter", default="train/lora_adapter_v6_claude")
    ap.add_argument("--only", default="01-word-count,03-sum-digits,05-gcd,11-env-or-default,21-invoice-total")
    ap.add_argument("--max-tokens", type=int, default=512)
    ap.add_argument("--out", default="")
    args = ap.parse_args()

    adapter_path = REPO_ROOT / args.adapter
    print(f"Loading {args.model} + {adapter_path}...", flush=True)
    model, tokenizer = load(args.model, adapter_path=str(adapter_path))
    sampler = make_sampler(temp=0.0)

    problem_ids = args.only.split(",")
    rows = []
    counts = {"total": 0, "instruction_followed": 0, "extracted_ok": 0, "verified": 0}

    for pid in problem_ids:
        candidates = list(BENCH_PROBLEMS.glob(f"{pid}*"))
        if not candidates:
            print(f"[skip] {pid} not found")
            continue
        d = candidates[0]
        objective = (d / "objective.md").read_text().strip()
        tests_ag = (d / "tests.ag").read_text().strip()
        user_prompt = build_prompt_no_tests(objective, tests_ag)
        chat = chat_format(tokenizer, SYSTEM_PROMPT, user_prompt)
        prompt_toks = len(tokenizer.encode(chat))

        t0 = time.monotonic()
        text = generate(model, tokenizer, prompt=chat,
                        max_tokens=args.max_tokens, sampler=sampler, verbose=False)
        wall = time.monotonic() - t0
        if text.startswith(chat): text = text[len(chat):]
        text = strip_im_end(text)
        gen_toks = len(tokenizer.encode(text))

        has_test = "(test " in text or "(test\n" in text
        defuns = extract_defuns(text)
        ok = False; passed = 0; total = 0; err = ""
        if defuns:
            assembled = assemble(defuns, tests_ag)
            ok, passed, total, err = verify(assembled)

        counts["total"] += 1
        if not has_test: counts["instruction_followed"] += 1
        if defuns: counts["extracted_ok"] += 1
        if ok: counts["verified"] += 1

        flag = "PASS" if ok else "FAIL"
        print(f"[{flag}] {d.name:30} wall={wall:5.1f}s "
              f"prompt_tok={prompt_toks:4d} gen_tok={gen_toks:4d} "
              f"instr_ok={'Y' if not has_test else 'N'} "
              f"extracted={'Y' if defuns else 'N'} "
              f"verified={passed}/{total}")
        if not defuns:
            print(f"  raw model output (first 400 chars):\n  {text[:400]!r}")
        elif has_test:
            print(f"  WARNING: model emitted (test ...) blocks despite instruction")
        elif not ok:
            print(f"  verify err: {err[:240]}")

        rows.append({
            "id": d.name, "wall_s": round(wall, 2),
            "prompt_tokens": prompt_toks, "gen_tokens": gen_toks,
            "instruction_followed": not has_test,
            "extracted": bool(defuns),
            "verified": ok, "passed": passed, "total": total,
            "raw_output_chars": len(text),
        })

    print()
    print(f"=== SUMMARY ({args.adapter}) ===")
    for k, v in counts.items():
        print(f"  {k:20} {v}/{counts['total']}")

    if args.out:
        out_path = Path(args.out)
        out_path.parent.mkdir(parents=True, exist_ok=True)
        with out_path.open("w") as f:
            for r in rows: f.write(json.dumps(r) + "\n")
        print(f"  results -> {out_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
