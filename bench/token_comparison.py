#!/usr/bin/env python3
"""Token-cost comparison: AGC vs Python vs TypeScript vs C#.

For each of 10 representative bench problems, we hold the FUNCTION-LEVEL
implementation (no tests, no boilerplate) and tokenize with cl100k_base
(OpenAI's tokenizer; widely cited as a fair reference for code).

Run:
  python3 bench/token_comparison.py
"""
from __future__ import annotations

import statistics
import tiktoken


tok = tiktoken.get_encoding("cl100k_base")


def t(s: str) -> int:
    return len(tok.encode(s))


# ---------- Problem implementations (function only, idiomatic per language) ----------

PROBLEMS: list[dict[str, str]] = [
    # ===== Pure =====
    {
        "id": "04-is-prime",
        "category": "pure",
        "agc": """(defun is_prime ((n : Num)) : Num
  (if (< n 2) (return 0) 0)
  (if (< n 4) (return 1) 0)
  (if (= (math.mod n 2) 0) (return 0) 0)
  (def i : Num 3)
  (while (<= (* i i) n)
    (do
      (if (= (math.mod n i) 0) (return 0) 0)
      (set i (+ i 2))))
  (return 1))""",
        "python": """def is_prime(n: int) -> int:
    if n < 2:
        return 0
    if n < 4:
        return 1
    if n % 2 == 0:
        return 0
    i = 3
    while i * i <= n:
        if n % i == 0:
            return 0
        i += 2
    return 1
""",
        "typescript": """export function isPrime(n: number): number {
    if (n < 2) return 0;
    if (n < 4) return 1;
    if (n % 2 === 0) return 0;
    for (let i = 3; i * i <= n; i += 2) {
        if (n % i === 0) return 0;
    }
    return 1;
}
""",
        "csharp": """public static int IsPrime(int n) {
    if (n < 2) return 0;
    if (n < 4) return 1;
    if (n % 2 == 0) return 0;
    for (int i = 3; i * i <= n; i += 2) {
        if (n % i == 0) return 0;
    }
    return 1;
}
""",
    },
    {
        "id": "06-fibonacci",
        "category": "pure",
        "agc": """(defun fib ((n : Num)) : Num
  (if (= n 0) (return 0) 0)
  (def a : Num 0)
  (def b : Num 1)
  (def i : Num 1)
  (while (< i n)
    (do
      (def t : Num (+ a b))
      (set a b)
      (set b t)
      (set i (+ i 1))))
  (return b))""",
        "python": """def fib(n: int) -> int:
    if n == 0:
        return 0
    a, b = 0, 1
    for _ in range(n - 1):
        a, b = b, a + b
    return b
""",
        "typescript": """export function fib(n: number): number {
    if (n === 0) return 0;
    let a = 0, b = 1;
    for (let i = 1; i < n; i++) {
        const t = a + b;
        a = b;
        b = t;
    }
    return b;
}
""",
        "csharp": """public static long Fib(int n) {
    if (n == 0) return 0;
    long a = 0, b = 1;
    for (int i = 1; i < n; i++) {
        long t = a + b;
        a = b;
        b = t;
    }
    return b;
}
""",
    },
    {
        "id": "09-count-char",
        "category": "pure",
        "agc": """(defun count_char ((s : Str) (c : Str)) : Num
  (def n : Num (str.length s))
  (def k : Num 0)
  (def i : Num 0)
  (while (< i n)
    (do
      (if (= (str.substring s i 1) c) (set k (+ k 1)) 0)
      (set i (+ i 1))))
  (return k))""",
        "python": """def count_char(s: str, c: str) -> int:
    return sum(1 for ch in s if ch == c)
""",
        "typescript": """export function countChar(s: string, c: string): number {
    let k = 0;
    for (const ch of s) if (ch === c) k++;
    return k;
}
""",
        "csharp": """public static int CountChar(string s, string c) {
    int k = 0;
    foreach (var ch in s) if (ch.ToString() == c) k++;
    return k;
}
""",
    },
    {
        "id": "10-reverse-string",
        "category": "pure",
        "agc": """(defun reverse_string ((s : Str)) : Str
  (def n : Num (str.length s))
  (def out : Str "")
  (def i : Num 0)
  (while (< i n)
    (do
      (set out (str.concat (str.substring s i 1) out))
      (set i (+ i 1))))
  (return out))""",
        "python": """def reverse_string(s: str) -> str:
    return s[::-1]
""",
        "typescript": """export function reverseString(s: string): string {
    return [...s].reverse().join("");
}
""",
        "csharp": """public static string ReverseString(string s) {
    var arr = s.ToCharArray();
    Array.Reverse(arr);
    return new string(arr);
}
""",
    },
    # ===== Capability =====
    {
        "id": "13-env-int-or",
        "category": "cap",
        "agc": """(extern defun env_read ((key : Str)) : Str @capability "env.get")
(defun port_or ((default : Num)) : Num
  (def v : Str (env_read "PORT"))
  (if (= v "") (return default) 0)
  (return (str.to_num v)))""",
        "python": """import os

def port_or(default: int) -> int:
    v = os.environ.get("PORT", "")
    if v == "":
        return default
    return int(v)
""",
        "typescript": """export function portOr(defaultPort: number): number {
    const v = process.env.PORT ?? "";
    if (v === "") return defaultPort;
    return parseInt(v, 10);
}
""",
        "csharp": """using System;

public static int PortOr(int defaultPort) {
    var v = Environment.GetEnvironmentVariable("PORT") ?? "";
    if (v == "") return defaultPort;
    return int.Parse(v);
}
""",
    },
    {
        "id": "17-file-line-count",
        "category": "cap",
        "agc": """(extern defun file_read ((path : Str)) : Str @capability "file.read")
(defun line_count ((path : Str)) : Num
  (def s : Str (file_read path))
  (def n : Num (str.length s))
  (if (= n 0) (return 0) 0)
  (def c : Num 0)
  (def i : Num 0)
  (while (< i n)
    (do
      (if (= (str.substring s i 1) "\\n") (set c (+ c 1)) 0)
      (set i (+ i 1))))
  (if (= (str.substring s (- n 1) 1) "\\n") (return c) (return (+ c 1))))""",
        "python": """def line_count(path: str) -> int:
    with open(path) as f:
        s = f.read()
    if not s:
        return 0
    c = s.count("\\n")
    return c if s.endswith("\\n") else c + 1
""",
        "typescript": """import * as fs from "fs";

export function lineCount(path: string): number {
    const s = fs.readFileSync(path, "utf8");
    if (s.length === 0) return 0;
    const c = (s.match(/\\n/g) || []).length;
    return s.endsWith("\\n") ? c : c + 1;
}
""",
        "csharp": """using System.IO;

public static int LineCount(string path) {
    var s = File.ReadAllText(path);
    if (s.Length == 0) return 0;
    int c = s.Split('\\n').Length - 1;
    return s.EndsWith("\\n") ? c : c + 1;
}
""",
    },
    {
        "id": "20-file-copy",
        "category": "cap",
        "agc": """(extern defun file_read  ((path : Str)) : Str @capability "file.read")
(extern defun file_write ((path : Str) (body : Str)) : Num @capability "file.write")
(defun copy_file ((src : Str) (dst : Str)) : Num
  (def s : Str (file_read src))
  (file_write dst s)
  (return (str.length s)))""",
        "python": """def copy_file(src: str, dst: str) -> int:
    with open(src) as f:
        s = f.read()
    with open(dst, "w") as f:
        f.write(s)
    return len(s)
""",
        "typescript": """import * as fs from "fs";

export function copyFile(src: string, dst: string): number {
    const s = fs.readFileSync(src, "utf8");
    fs.writeFileSync(dst, s);
    return s.length;
}
""",
        "csharp": """using System.IO;

public static int CopyFile(string src, string dst) {
    var s = File.ReadAllText(src);
    File.WriteAllText(dst, s);
    return s.Length;
}
""",
    },
    # ===== Multi-rule =====
    {
        "id": "21-invoice-total",
        "category": "multi",
        "agc": """(defun round2 ((x : Num)) : Num
  (return (/ (math.floor (+ (* x 100.0) 0.5)) 100.0)))
(defun invoice_total ((unit_price : Num) (quantity : Num) (discount_pct : Num) (tax_pct : Num) (shipping : Num)) : Num
  (def subtotal : Num (* unit_price quantity))
  (def discounted : Num (* subtotal (- 1.0 (/ discount_pct 100.0))))
  (def taxed : Num (* discounted (+ 1.0 (/ tax_pct 100.0))))
  (return (round2 (+ taxed shipping))))""",
        "python": """def invoice_total(unit_price: float, quantity: float, discount_pct: float, tax_pct: float, shipping: float) -> float:
    subtotal = unit_price * quantity
    discounted = subtotal * (1 - discount_pct / 100)
    taxed = discounted * (1 + tax_pct / 100)
    return round(taxed + shipping, 2)
""",
        "typescript": """export function invoiceTotal(unitPrice: number, quantity: number, discountPct: number, taxPct: number, shipping: number): number {
    const subtotal = unitPrice * quantity;
    const discounted = subtotal * (1 - discountPct / 100);
    const taxed = discounted * (1 + taxPct / 100);
    return Math.round((taxed + shipping) * 100) / 100;
}
""",
        "csharp": """public static double InvoiceTotal(double unitPrice, double quantity, double discountPct, double taxPct, double shipping) {
    double subtotal = unitPrice * quantity;
    double discounted = subtotal * (1 - discountPct / 100);
    double taxed = discounted * (1 + taxPct / 100);
    return Math.Round(taxed + shipping, 2);
}
""",
    },
    {
        "id": "23-rental-cost",
        "category": "multi",
        "agc": """(defun round2 ((x : Num)) : Num
  (return (/ (math.floor (+ (* x 100.0) 0.5)) 100.0)))
(defun period_base ((days : Num) (daily : Num)) : Num
  (def weeks : Num (math.floor (/ days 7.0)))
  (def rem : Num (- days (* weeks 7.0)))
  (return (+ (* weeks (* 6.0 daily)) (* rem daily))))
(defun rental_cost ((days : Num) (daily : Num) (insurance : Num) (loyalty : Num) (tax_pct : Num)) : Num
  (def base : Num (period_base days daily))
  (def with_ins : Num (+ base (* days insurance)))
  (def discounted : Num (if (= loyalty 1) (* with_ins 0.9) with_ins))
  (def taxed : Num (* discounted (+ 1.0 (/ tax_pct 100.0))))
  (return (round2 taxed)))""",
        "python": """import math

def rental_cost(days: float, daily: float, insurance: float, loyalty: int, tax_pct: float) -> float:
    weeks = math.floor(days / 7)
    rem = days - weeks * 7
    base = weeks * 6 * daily + rem * daily
    with_ins = base + days * insurance
    discounted = with_ins * 0.9 if loyalty == 1 else with_ins
    taxed = discounted * (1 + tax_pct / 100)
    return round(taxed, 2)
""",
        "typescript": """export function rentalCost(days: number, daily: number, insurance: number, loyalty: number, taxPct: number): number {
    const weeks = Math.floor(days / 7);
    const rem = days - weeks * 7;
    const base = weeks * 6 * daily + rem * daily;
    const withIns = base + days * insurance;
    const discounted = loyalty === 1 ? withIns * 0.9 : withIns;
    const taxed = discounted * (1 + taxPct / 100);
    return Math.round(taxed * 100) / 100;
}
""",
        "csharp": """public static double RentalCost(double days, double daily, double insurance, int loyalty, double taxPct) {
    int weeks = (int)Math.Floor(days / 7);
    double rem = days - weeks * 7;
    double base_ = weeks * 6 * daily + rem * daily;
    double withIns = base_ + days * insurance;
    double discounted = loyalty == 1 ? withIns * 0.9 : withIns;
    double taxed = discounted * (1 + taxPct / 100);
    return Math.Round(taxed, 2);
}
""",
    },
    {
        "id": "26-tax-progressive",
        "category": "multi",
        "agc": """(defun round2 ((x : Num)) : Num
  (return (/ (math.floor (+ (* x 100.0) 0.5)) 100.0)))
(defun income_tax ((income : Num)) : Num
  (def t1 : Num (* (math.min income 10000.0) 0.10))
  (def t2 : Num (* (math.max 0.0 (- (math.min income 50000.0) 10000.0)) 0.20))
  (def t3 : Num (* (math.max 0.0 (- income 50000.0)) 0.30))
  (return (round2 (+ (+ t1 t2) t3))))""",
        "python": """def income_tax(income: float) -> float:
    t1 = min(income, 10000) * 0.10
    t2 = max(0, min(income, 50000) - 10000) * 0.20
    t3 = max(0, income - 50000) * 0.30
    return round(t1 + t2 + t3, 2)
""",
        "typescript": """export function incomeTax(income: number): number {
    const t1 = Math.min(income, 10000) * 0.10;
    const t2 = Math.max(0, Math.min(income, 50000) - 10000) * 0.20;
    const t3 = Math.max(0, income - 50000) * 0.30;
    return Math.round((t1 + t2 + t3) * 100) / 100;
}
""",
        "csharp": """public static double IncomeTax(double income) {
    double t1 = Math.Min(income, 10000) * 0.10;
    double t2 = Math.Max(0, Math.Min(income, 50000) - 10000) * 0.20;
    double t3 = Math.Max(0, income - 50000) * 0.30;
    return Math.Round(t1 + t2 + t3, 2);
}
""",
    },
]


# ---------- Tokenize and report ----------


def main() -> None:
    LANGS = ["agc", "python", "typescript", "csharp"]
    print(f"\n{'problem':22}  {'cat':>5}  " + "  ".join(f"{lang:>11}" for lang in LANGS))
    print("-" * 76)

    by_lang: dict[str, list[int]] = {l: [] for l in LANGS}
    for prob in PROBLEMS:
        counts = {l: t(prob[l]) for l in LANGS}
        for l in LANGS:
            by_lang[l].append(counts[l])
        cells = "  ".join(f"{counts[l]:>11}" for l in LANGS)
        print(f"{prob['id']:22}  {prob['category']:>5}  {cells}")

    print("-" * 76)
    print(f"{'MEDIAN':22}  {'':>5}  " + "  ".join(
        f"{int(statistics.median(by_lang[l])):>11}" for l in LANGS))
    print(f"{'MEAN':22}  {'':>5}  " + "  ".join(
        f"{int(statistics.mean(by_lang[l])):>11}" for l in LANGS))
    print(f"{'TOTAL':22}  {'':>5}  " + "  ".join(
        f"{sum(by_lang[l]):>11}" for l in LANGS))

    print()
    print(f"AGC vs other languages (ratio of total tokens):")
    agc_total = sum(by_lang["agc"])
    for l in ["python", "typescript", "csharp"]:
        ratio = sum(by_lang[l]) / agc_total
        print(f"  {l:>10} / agc = {ratio:.2f}x")


if __name__ == "__main__":
    main()
