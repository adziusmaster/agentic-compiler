#!/usr/bin/env python3
"""Generate a verified AGC training dataset.

Loop:
  1. Ask Gemini to propose an AGC problem (objective + solution + tests) in
     one of three categories (pure | cap | multi).
  2. Write the solution to a temp file; run `agc check` on it.
  3. If every test passes, append the verified pair to dataset/agc_pairs.jsonl.

Target: ~1500 verified pairs (500 per category).
Cost estimate on Gemini 2.5 Flash: ~$2 one-time.

Usage:
  SSL_CERT_FILE=/etc/ssl/cert.pem GEMINI_API_KEY=... \\
      python3 generate_dataset.py --count 1500 --workers 8
"""
from __future__ import annotations

import argparse
import json
import os
import random
import re
import subprocess
import tempfile
import time
import urllib.request
from concurrent.futures import ThreadPoolExecutor, wait, FIRST_COMPLETED
from pathlib import Path
from threading import Lock

TRAIN_DIR = Path(__file__).resolve().parent
DATASET_PATH = TRAIN_DIR / "dataset" / "agc_pairs.jsonl"
REPO_ROOT = TRAIN_DIR.parent
AGC_PROJECT = REPO_ROOT / "Agentic.Cli"
# Use the already-built DLL directly, not `dotnet run` (which forks MSBuild).
AGC_CLI_DLL = AGC_PROJECT / "bin" / "Debug" / "net8.0" / "Agentic.Cli.dll"

PURE_TOPICS = [
    "celsius to fahrenheit", "kelvin conversion",
    "miles to kilometers", "kilos to pounds", "meters to feet",
    "factorial", "power of two check", "odd or even", "digit sum",
    "count vowels", "count consonants", "palindrome check",
    "fizzbuzz", "roman numeral parse", "leap year check",
    "triangle area heron", "circle area", "rectangle perimeter",
    "hypotenuse", "quadratic discriminant", "absolute difference",
    "max of three", "median of three", "sign function",
    "clamp to range", "lerp between two numbers",
    "percentage change",
    "string length without str.length", "capitalize first letter",
    "repeat string n times", "trim whitespace manual",
    "sum of array", "product of array", "count matches in array",
    "binary search array", "max element of array", "min element of array",
    "reverse array of numbers", "linear search in array",
    "count evens in array", "count zeros in array",
    "array contains element", "index of element in array",
    "is array sorted ascending", "scalar multiply array",
    "subtract constant from array", "array dot product (fixed-length)",
    "average of array values", "median of three via array",
    "fibonacci iterative", "collatz steps", "digital root",
    "is prime trial division", "gcd euclidean", "lcm", "power of integer",
]

CAP_TOPICS = [
    "read env var with default", "read env var as int", "read env var bool",
    "read two env vars and concat", "env var uppercased", "env var exists check",
    "read first line of file", "count lines in file", "read file byte length",
    "read file and count char occurrences", "file exists check",
    "write fixed string to file",
    "http GET body length", "http GET status as string",
    "db scalar count", "db exists check",
]

MULTI_TOPICS = [
    "shopping cart total with tax and discount",
    "compute monthly rent with utilities",
    "credit card minimum payment",
    "bmi classification",
    "tax brackets progressive",
    "speed from distance and time",
    "paycheck net from hours and rate",
    "compound interest",
    "loan monthly payment",
    "tip split among diners",
    "shipping cost by weight and zone",
    "heart rate zone classification",
    "grade point average",
    "fuel cost for trip",
    "electricity bill tiered",
    "recipe scale by servings",
    "currency exchange with fee",
    "sales commission tiered",
    "course grade weighted components",
    "water intake goal by weight",
    "price with bulk discount",
    "hotel rate weekend surcharge",
    "parking cost first-hour free",
    "phone plan with overage",
    "ride fare base plus distance",
    "gym membership with joining fee",
]

FEW_SHOT = {
    "pure": [
"""(module DigitSum
  (defun digit_sum ((n : Num)) : Num
    (def acc : Num 0)
    (def cur : Num n)
    (while (> cur 0) (do
      (def acc : Num (+ acc (math.mod cur 10)))
      (def cur : Num (math.floor (/ cur 10)))))
    (return acc))

  (test zero (assert-eq (digit_sum 0) 0))
  (test small (assert-eq (digit_sum 7) 7))
  (test two_digit (assert-eq (digit_sum 42) 6))
  (test big (assert-eq (digit_sum 12345) 15)))""",
"""(module IsPrime
  (defun is_prime ((n : Num)) : Num
    (if (< n 2) (return 0) 0)
    (if (= n 2) (return 1) 0)
    (if (= (math.mod n 2) 0) (return 0) 0)
    (def i : Num 3)
    (def bound : Num (math.floor (math.sqrt n)))
    (while (<= i bound) (do
      (if (= (math.mod n i) 0) (return 0) 0)
      (def i : Num (+ i 2))))
    (return 1))

  (test two (assert-eq (is_prime 2) 1))
  (test four (assert-eq (is_prime 4) 0))
  (test seventeen (assert-eq (is_prime 17) 1)))""",
"""(module ArraySum
  (defun sum ((xs : (Array Num))) : Num
    (def i : Num 0)
    (def n : Num (arr.length xs))
    (def total : Num 0)
    (while (< i n) (do
      (def total : Num (+ total (arr.get xs i)))
      (def i : Num (+ i 1))))
    (return total))

  (defun make3 ((a : Num) (b : Num) (c : Num)) : (Array Num)
    (def xs : (Array Num) (arr.new 3))
    (arr.set xs 0 a)
    (arr.set xs 1 b)
    (arr.set xs 2 c)
    (return xs))

  (test empty (assert-eq (sum (arr.new 0)) 0))
  (test one   (assert-eq (sum (make3 5 0 0)) 5))
  (test three (assert-eq (sum (make3 1 2 3)) 6))
  (test negs  (assert-eq (sum (make3 -1 -2 -3)) -6)))""",
    ],
    "cap": [
"""(module EnvOrDefault
  (extern defun env_read ((key : Str)) : Str @capability "env.get")

  (defun get_or ((key : Str) (default : Str)) : Str
    (def val : Str (env_read key))
    (if (str.eq val "") (return default) (return val)))

  (test missing (mocks (env.get "NAME" "")) (assert-eq (get_or "NAME" "anon") "anon"))
  (test present (mocks (env.get "NAME" "Ada")) (assert-eq (get_or "NAME" "anon") "Ada")))""",
"""(module EnvIntOr
  (extern defun env_read ((key : Str)) : Str @capability "env.get")

  (defun port_or ((key : Str) (default : Num)) : Num
    (def val : Str (env_read key))
    (if (str.eq val "") (return default) (return (str.to_num val))))

  (test missing (mocks (env.get "PORT" "")) (assert-eq (port_or "PORT" 8080) 8080))
  (test present (mocks (env.get "PORT" "3000")) (assert-eq (port_or "PORT" 8080) 3000))
  (test zero    (mocks (env.get "PORT" "0")) (assert-eq (port_or "PORT" 8080) 0)))""",
"""(module FileLineCount
  (extern defun file_read ((path : Str)) : Str @capability "file.read")

  (defun line_count ((path : Str)) : Num
    (def content : Str (file_read path))
    (def n : Num (str.length content))
    (def i : Num 0)
    (def count : Num 0)
    (while (< i n) (do
      (if (str.eq (str.substring content i 1) "\\n")
        (def count : Num (+ count 1))
        0)
      (def i : Num (+ i 1))))
    (return count))

  (test empty (mocks (file.read "/f" "")) (assert-eq (line_count "/f") 0))
  (test one   (mocks (file.read "/f" "a\\n")) (assert-eq (line_count "/f") 1))
  (test two   (mocks (file.read "/f" "a\\nb\\n")) (assert-eq (line_count "/f") 2)))""",
"""(module DbUrl
  (extern defun env_read ((key : Str)) : Str @capability "env.get")

  (defun db_url () : Str
    (def h : Str (env_read "DB_HOST"))
    (def p : Str (env_read "DB_PORT"))
    (if (str.eq h "") (return "localhost:5432") 0)
    (if (str.eq p "") (return "localhost:5432") 0)
    (return (str.concat h (str.concat ":" p))))

  (test both_present
    (mocks (env.get "DB_HOST" "db.example.com")
           (env.get "DB_PORT" "5555"))
    (assert-eq (db_url) "db.example.com:5555"))
  (test host_missing
    (mocks (env.get "DB_HOST" "")
           (env.get "DB_PORT" "5555"))
    (assert-eq (db_url) "localhost:5432")))""",
"""(module FileCopy
  (extern defun file_read ((path : Str)) : Str @capability "file.read")
  (extern defun file_write ((path : Str) (data : Str)) : Num @capability "file.write")

  (defun copy_upper ((src : Str) (dst : Str)) : Num
    (def content : Str (file_read src))
    (def upper : Str (str.upper content))
    (return (file_write dst upper)))

  (test basic
    (mocks (file.read "/a" "hello")
           (file.write "/b" "HELLO" 5))
    (assert-eq (copy_upper "/a" "/b") 5)))""",
    ],
    "multi": [
"""(module InvoiceTotal
  (defun subtotal ((unit : Num) (qty : Num)) : Num
    (return (* unit qty)))

  (defun apply_discount ((amount : Num) (pct : Num)) : Num
    (return (- amount (/ (* amount pct) 100))))

  (defun apply_tax ((amount : Num) (pct : Num)) : Num
    (return (* amount (+ 1 (/ pct 100)))))

  (defun round2 ((x : Num)) : Num
    (return (/ (math.floor (+ (* x 100) 0.5)) 100)))

  (defun invoice_total ((unit : Num) (qty : Num) (disc : Num) (tax : Num) (ship : Num)) : Num
    (def s : Num (subtotal unit qty))
    (def d : Num (apply_discount s disc))
    (def t : Num (apply_tax d tax))
    (return (round2 (+ t ship))))

  (test plain (assert-near (invoice_total 10 2 0 0 0) 20 0.01))
  (test full (assert-near (invoice_total 100 3 10 20 5) 329 0.01)))""",
    ],
}

SYSTEM = """You are generating AGC (Agentic Compiler) training data.

AGC is an S-expression language. Strict conventions:
- Type annotations use `: Type` (never `->`). Every parameter MUST be annotated.
- Types: Num, Str, Bool, (Array T).
- Forms: (defun name ((p : T)...) : T body), (def name : T expr),
  (if c then else), (while c (do ...)), (return e), (require c).
- Math is strictly binary: (+ a (+ b c)) never (+ a b c).
- Stdlib: math.* (pow floor sqrt mod abs min max), str.* (eq split join substring to_num from_num lower upper length index_of concat), arr.* (new get set length).
- Tests: (test name (assert-eq actual expected)) or (assert-near a b tol).
- Capability FFI: (extern defun name ((p : T)) : T @capability "cap.name"), with (mocks (cap.name args return)) in tests.
- Do NOT use list.*, prose comments, markdown fences, sys.input.get, or sys.stdout.write.

IMPORTANT RUNTIME SAFETY:
- str.to_num THROWS on non-numeric input. NEVER feed it a value that cannot parse (like "abc", "xyz", "N/A"). If you need fault tolerance, branch with (str.eq ...) first.
- arr.get THROWS on out-of-bounds indexes. Only index within (arr.length arr).
- math.mod / math.sqrt / division THROW on zero or negatives where undefined.
- Every (mocks ...) value is used as-is. The mocked return string is fed directly into the function — make sure your tests only use mocked returns that the function can handle without throwing.

CAPABILITY (cap) RULES — read carefully:
- Declare each capability EXACTLY once at top of module: (extern defun NAME ((p1 : T1)) : T2 @capability "cap.name")
- Call it like a normal defun: (NAME arg1). Arity MUST match the extern declaration.
- In tests, use (mocks (cap.name arg ret-value) [(cap.name arg ret-value) ...]) inside each (test ...) block.
- Mock keys: cap.name is the CAPABILITY string (e.g. "env.get", "file.read"), NOT the extern's local name.
- Mock args MUST match how you call the extern at runtime (same strings/numbers). The mock returns ret-value as the extern's return on that call.
- Only use capabilities: env.get, file.read, file.write, file.exists, http.fetch, db.query.
- NEVER pass default/fallback VALUES to the extern — externs only accept the arguments they're declared with. Handle defaults in your pure defun by branching on the empty string.
- Each (test ...) should supply mocks for every capability call the function makes."""

TEMPLATE = """Generate ONE AGC programming problem in category "{category}".

Topic hint: {topic}

Output format — TWO sections separated by a line containing only `===`:

1. Objective: a 1-3 sentence natural-language description.
2. A complete AGC module source:
   - Module name in CamelCase.
   - 1-6 defuns (or for cap: one extern defun + 1-3 helpers).
   - 3-7 (test ...) blocks using assert-eq or assert-near.
   - For cap: use (mocks ...) inside EVERY test.
   - No main block.

Reference examples:

{examples}

Now generate a new "{category}" problem about "{topic}". Different from the examples. Output objective, `===`, then the module."""


def call_gemini(prompt: str, timeout: int = 60) -> tuple[str, int, int]:
    key = os.environ["GEMINI_API_KEY"]
    model = os.environ.get("GEN_MODEL", "gemini-2.5-flash")
    url = (f"https://generativelanguage.googleapis.com/v1beta/models/"
           f"{model}:generateContent?key={key}")
    body = {
        "systemInstruction": {"parts": [{"text": SYSTEM}]},
        "contents": [{"role": "user", "parts": [{"text": prompt}]}],
        "generationConfig": {"maxOutputTokens": 4096, "temperature": 0.8},
    }
    req = urllib.request.Request(
        url, data=json.dumps(body).encode(),
        headers={"content-type": "application/json"},
    )
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        data = json.loads(resp.read())
    cands = data.get("candidates", [])
    text = ""
    if cands:
        parts = cands[0].get("content", {}).get("parts", [])
        text = "".join(p.get("text", "") for p in parts)
    usage = data.get("usageMetadata", {})
    return text, usage.get("promptTokenCount", 0), usage.get("candidatesTokenCount", 0)


def build_prompt(category: str, topic: str) -> str:
    examples = "\n\n".join(random.sample(FEW_SHOT[category], k=min(2, len(FEW_SHOT[category]))))
    return TEMPLATE.format(category=category, topic=topic, examples=examples)


TESTS_OK_RE = re.compile(r"\(ok \(tests-passed (\d+)/(\d+)\)\)")


def verify(source: str) -> tuple[bool, int, int, str]:
    with tempfile.NamedTemporaryFile("w", suffix=".ag", delete=False, dir="/tmp") as f:
        f.write(source)
        path = f.name
    # Grant all capability permissions for the verifier — we're evaluating
    # correctness (mocks run in-proc), not enforcing policy.
    cmd = ["dotnet", str(AGC_CLI_DLL), "check", path,
           "--allow-env", "--allow-file", "--allow-http", "--allow-db"]
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True, timeout=20)
        out = proc.stdout + "\n" + proc.stderr
        m = TESTS_OK_RE.search(out)
        if m and int(m.group(1)) == int(m.group(2)) and int(m.group(2)) > 0:
            return True, int(m.group(1)), int(m.group(2)), ""
        return False, 0, 0, out[-300:]
    except subprocess.TimeoutExpired:
        return False, 0, 0, "timeout"
    finally:
        try: os.unlink(path)
        except Exception: pass


def parse_response(text: str) -> tuple[str, str] | None:
    parts = text.split("===", 1)
    if len(parts) != 2:
        return None
    obj = parts[0].strip().removeprefix("Objective:").strip()
    sol = parts[1].strip()
    sol = re.sub(r"^```\w*\n?", "", sol)
    sol = re.sub(r"\n?```$", "", sol)
    sol = sol.strip()
    if not sol.startswith("(module"):
        return None
    return obj, sol


def generate_one(category: str, topic_pool: list[str]) -> dict:
    topic = random.choice(topic_pool)
    try:
        text, tin, tout = call_gemini(build_prompt(category, topic))
    except Exception as e:
        return {"error": f"api:{e}", "category": category, "topic": topic}
    parsed = parse_response(text)
    if not parsed:
        return {"error": "parse_failed", "category": category, "topic": topic}
    obj, sol = parsed
    ok, passed, total, err_tail = verify(sol)
    if not ok:
        return {"error": "verify_failed", "category": category, "topic": topic,
                "err_tail": err_tail[:200]}
    return {
        "category": category, "topic": topic,
        "objective": obj, "solution": sol, "tests_passed": passed,
        "tokens_in": tin, "tokens_out": tout,
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--count", type=int, default=1500)
    ap.add_argument("--workers", type=int, default=4)
    ap.add_argument("--category", choices=["pure", "cap", "multi"],
                    help="only generate one category (for gap-filling runs)")
    ap.add_argument("--model",
                    help="override GEN_MODEL env var (e.g. gemini-2.5-pro for cap)")
    args = ap.parse_args()
    if args.model:
        os.environ["GEN_MODEL"] = args.model

    DATASET_PATH.parent.mkdir(parents=True, exist_ok=True)
    by_cat = {"pure": 0, "cap": 0, "multi": 0}
    if DATASET_PATH.exists():
        for line in DATASET_PATH.open():
            c = json.loads(line).get("category", "")
            if c in by_cat:
                by_cat[c] += 1
    verified = sum(by_cat.values())
    print(f"Starting with {verified} verified pairs: {by_cat}; target={args.count}")

    target_per_cat = args.count // 3
    pools = {"pure": PURE_TOPICS, "cap": CAP_TOPICS, "multi": MULTI_TOPICS}

    lock = Lock()
    skipped = {"parse_failed": 0, "verify_failed": 0, "api": 0}
    attempts = 0
    # Per-category failure streak — temporarily disable a category that's
    # bleeding budget with zero yield (e.g. Gemini can't write valid `mocks`).
    fail_streak = {"pure": 0, "cap": 0, "multi": 0}
    MAX_DEAD_STREAK = 40  # disable a category after 40 consecutive fails
    t0 = time.monotonic()

    def pick_live_category() -> str:
        # --category pins all generation to one class (gap-filling mode).
        if args.category:
            return args.category
        remaining = []
        for c in by_cat:
            need = max(0, target_per_cat - by_cat[c])
            alive = fail_streak[c] < MAX_DEAD_STREAK
            remaining.append((c, need if alive else 0))
        cats, weights = zip(*remaining)
        if sum(weights) == 0:
            live = [c for c in by_cat if fail_streak[c] < MAX_DEAD_STREAK]
            return random.choice(live) if live else random.choice(list(by_cat.keys()))
        return random.choices(cats, weights=weights)[0]

    def task():
        c = pick_live_category()
        return generate_one(c, pools[c])

    with ThreadPoolExecutor(max_workers=args.workers) as ex:
        pending = {ex.submit(task) for _ in range(args.workers)}
        while verified < args.count and pending:
            done, pending = wait(pending, return_when=FIRST_COMPLETED)
            for f in done:
                attempts += 1
                r = f.result()
                cat_hint = r.get("category", "")
                if "error" in r:
                    kind = r["error"].split(":", 1)[0]
                    skipped[kind] = skipped.get(kind, 0) + 1
                    if cat_hint in fail_streak:
                        fail_streak[cat_hint] += 1
                else:
                    with lock:
                        with DATASET_PATH.open("a") as fh:
                            fh.write(json.dumps({
                                "category": r["category"], "topic": r["topic"],
                                "objective": r["objective"], "solution": r["solution"],
                                "tests_passed": r["tests_passed"],
                            }) + "\n")
                        verified += 1
                        by_cat[r["category"]] += 1
                    fail_streak[r["category"]] = 0
                if attempts % 5 == 0:
                    rate = verified / ((time.monotonic() - t0) / 60 + 1e-9)
                    disabled = [c for c, s in fail_streak.items() if s >= MAX_DEAD_STREAK]
                    print(f"  [{attempts}] verified={verified}/{args.count} "
                          f"by_cat={by_cat} skip={skipped} "
                          f"streak={fail_streak} disabled={disabled} "
                          f"rate={rate:.1f}/min", flush=True)
                if verified < args.count:
                    pending.add(ex.submit(task))

    dt = (time.monotonic() - t0) / 60
    print(f"\nDone: {verified}/{args.count} in {dt:.1f} min. skip={skipped} by_cat={by_cat}")
    print(f"  dataset: {DATASET_PATH}")


if __name__ == "__main__":
    main()
