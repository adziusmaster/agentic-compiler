#!/usr/bin/env python3
"""Batch v6: edge-case and multi-rule decomposition anchors.

Targets the partial-fail patterns observed on bench:
  - whitespace-collapsing word count (01)
  - balanced parens (07)
  - bracket-piecewise tax / progressive math (26)
  - multi-rule paycheck/rental/shipping (22, 23, 28)
  - invoice with zero-quantity edge (21)

Each pair uses canonical idioms: math.min / math.max / math.floor
(NOT bare min/max or math_floor with underscore), function names match
test-call sites exactly, and edge cases (empty / zero / boundary) are
covered in explicit tests.
"""
from __future__ import annotations
import json
import sys


PAIRS: list[tuple[str, str, str, str]] = []


def add(category: str, topic: str, objective: str, solution: str) -> None:
    PAIRS.append((category, topic, objective, solution.strip()))


# ============== Whitespace-aware word count ==============

add("pure", "word_count_whitespace_collapse",
    "Count whitespace-separated words; consecutive spaces collapse, leading/trailing whitespace ignored, empty string returns 0.",
    """(module WordCount
  (defun is_ws (c)
    (if (= c " ") 1 (if (= c "\\t") 1 0)))
  (defun word_count (s)
    (def n (str_length s))
    (def c 0)
    (def i 0)
    (def in_word 0)
    (while (< i n)
      (do
        (def ch (str_substring s i 1))
        (if (= (is_ws ch) 1)
          (set in_word 0)
          (do
            (if (= in_word 0) (set c (+ c 1)) 0)
            (set in_word 1)))
        (set i (+ i 1))))
    c)
  (test empty (eq? (word_count "") 0))
  (test one (eq? (word_count "hello") 1))
  (test two (eq? (word_count "hello world") 2))
  (test leading (eq? (word_count "  hi") 1))
  (test trailing (eq? (word_count "hi  ") 1))
  (test collapse (eq? (word_count "a  b   c") 3))
  (test tabs (eq? (word_count "a\\tb\\tc") 3)))""")

add("pure", "char_count_excluding_spaces",
    "Count non-whitespace characters in s.",
    """(module NonWsCount
  (defun is_ws (c)
    (if (= c " ") 1 (if (= c "\\t") 1 0)))
  (defun nws_count (s)
    (def n (str_length s))
    (def c 0)
    (def i 0)
    (while (< i n)
      (do
        (if (= (is_ws (str_substring s i 1)) 0) (set c (+ c 1)) 0)
        (set i (+ i 1))))
    c)
  (test simple (eq? (nws_count "abc") 3))
  (test spaces (eq? (nws_count "a b c") 3))
  (test tabs (eq? (nws_count "a\\tb") 2))
  (test empty (eq? (nws_count "") 0)))""")

# ============== Balanced parens ==============

add("pure", "is_balanced_parens",
    "Return 1 if parentheses in s are balanced (other chars ignored), else 0. Empty string is balanced.",
    """(module Balanced
  (defun is_balanced (s)
    (def n (str_length s))
    (def depth 0)
    (def fail 0)
    (def i 0)
    (while (< i n)
      (do
        (def ch (str_substring s i 1))
        (if (= ch "(") (set depth (+ depth 1)) 0)
        (if (= ch ")")
          (do
            (if (= depth 0) (set fail 1) 0)
            (if (> depth 0) (set depth (- depth 1)) 0))
          0)
        (set i (+ i 1))))
    (if (= fail 1) (return 0))
    (if (= depth 0) 1 0))
  (test empty (eq? (is_balanced "") 1))
  (test pair (eq? (is_balanced "()") 1))
  (test nested (eq? (is_balanced "(())") 1))
  (test siblings (eq? (is_balanced "(()())") 1))
  (test ignore (eq? (is_balanced "(a(b)c)") 1))
  (test reversed (eq? (is_balanced ")(") 0))
  (test unclosed (eq? (is_balanced "((") 0))
  (test trailing (eq? (is_balanced "())") 0))
  (test bare_text (eq? (is_balanced "abc") 1)))""")

add("pure", "depth_of_parens",
    "Return the maximum nesting depth of parentheses in s.",
    """(module ParenDepth
  (defun max_depth (s)
    (def n (str_length s))
    (def cur 0)
    (def best 0)
    (def i 0)
    (while (< i n)
      (do
        (def ch (str_substring s i 1))
        (if (= ch "(") (do (set cur (+ cur 1)) (if (> cur best) (set best cur) 0)) 0)
        (if (= ch ")") (set cur (- cur 1)) 0)
        (set i (+ i 1))))
    best)
  (test empty (eq? (max_depth "") 0))
  (test one (eq? (max_depth "()") 1))
  (test deep (eq? (max_depth "((()))") 3))
  (test mixed (eq? (max_depth "(a(b)c)") 2)))""")

# ============== Banker's-style round to 2 decimals ==============

add("pure", "round_to_two_decimals",
    "Round a positive float to 2 decimal places (half-up).",
    """(module Round2
  (defun round2 (x)
    (/ (math.floor (+ (* x 100.0) 0.5)) 100.0))
  (test zero (near? (round2 0.0) 0.0 0.0001))
  (test exact (near? (round2 1.23) 1.23 0.0001))
  (test up (near? (round2 1.235) 1.24 0.0001))
  (test down (near? (round2 1.234) 1.23 0.0001))
  (test large (near? (round2 12345.678) 12345.68 0.0001)))""")

# ============== Progressive tax with three brackets ==============

add("multi", "progressive_three_brackets",
    "Three-bracket progressive tax: 10% on first 10K, 20% on next 40K, 30% above 50K. Round to 2 decimals.",
    """(module ProgressiveTax
  (defun round2 (x)
    (/ (math.floor (+ (* x 100.0) 0.5)) 100.0))
  (defun income_tax (income)
    (def t1 (* (math.min income 10000.0) 0.10))
    (def above_10k (math.max 0.0 (- (math.min income 50000.0) 10000.0)))
    (def t2 (* above_10k 0.20))
    (def above_50k (math.max 0.0 (- income 50000.0)))
    (def t3 (* above_50k 0.30))
    (round2 (+ (+ t1 t2) t3)))
  (test zero (near? (income_tax 0.0) 0.0 0.01))
  (test low (near? (income_tax 5000.0) 500.0 0.01))
  (test boundary1 (near? (income_tax 10000.0) 1000.0 0.01))
  (test mid (near? (income_tax 30000.0) 5000.0 0.01))
  (test boundary2 (near? (income_tax 50000.0) 9000.0 0.01))
  (test high (near? (income_tax 100000.0) 24000.0 0.01)))""")

add("multi", "two_bracket_tax",
    "Two-bracket tax: 15% up to 20K, 25% above. Round to 2 decimals.",
    """(module TwoBracketTax
  (defun round2 (x)
    (/ (math.floor (+ (* x 100.0) 0.5)) 100.0))
  (defun tax2 (income)
    (def low (* (math.min income 20000.0) 0.15))
    (def high (* (math.max 0.0 (- income 20000.0)) 0.25))
    (round2 (+ low high)))
  (test zero (near? (tax2 0.0) 0.0 0.01))
  (test low (near? (tax2 10000.0) 1500.0 0.01))
  (test boundary (near? (tax2 20000.0) 3000.0 0.01))
  (test high (near? (tax2 50000.0) 10500.0 0.01)))""")

# ============== Multi-rule pricing pipelines ==============

add("multi", "invoice_with_discount_and_tax_and_shipping",
    "Invoice total: subtotal × (1 - discount/100) × (1 + tax/100) + shipping. Round to 2 decimals.",
    """(module Invoice
  (defun round2 (x)
    (/ (math.floor (+ (* x 100.0) 0.5)) 100.0))
  (defun invoice_total (unit_price quantity discount_pct tax_pct shipping)
    (def subtotal (* unit_price quantity))
    (def disc (- subtotal (* subtotal (/ discount_pct 100.0))))
    (def taxed (* disc (+ 1.0 (/ tax_pct 100.0))))
    (round2 (+ taxed shipping)))
  (test plain (near? (invoice_total 10.0 2.0 0.0 0.0 0.0) 20.0 0.01))
  (test with_tax (near? (invoice_total 10.0 2.0 0.0 10.0 0.0) 22.0 0.01))
  (test with_discount (near? (invoice_total 10.0 2.0 10.0 0.0 0.0) 18.0 0.01))
  (test shipping_only (near? (invoice_total 0.0 0.0 0.0 0.0 5.0) 5.0 0.01))
  (test full (near? (invoice_total 100.0 3.0 10.0 20.0 5.0) 329.0 0.01))
  (test zero_qty (near? (invoice_total 50.0 0.0 10.0 20.0 5.0) 5.0 0.01)))""")

add("multi", "paycheck_with_overtime_and_taxes",
    "Net paycheck: gross with 1.5x overtime past 40h, then subtract FICA (7.65%), federal pct of (gross-FICA), state pct of gross.",
    """(module Paycheck
  (defun round2 (x)
    (/ (math.floor (+ (* x 100.0) 0.5)) 100.0))
  (defun gross_pay (hours rate)
    (def reg (* (math.min hours 40.0) rate))
    (def ot (* (math.max 0.0 (- hours 40.0)) (* rate 1.5)))
    (+ reg ot))
  (defun net_pay (hours rate fed_pct state_pct)
    (def gross (gross_pay hours rate))
    (def fica (* gross 0.0765))
    (def fed (* (- gross fica) (/ fed_pct 100.0)))
    (def state (* gross (/ state_pct 100.0)))
    (round2 (- (- (- gross fica) fed) state)))
  (test no_overtime (near? (net_pay 40.0 20.0 10.0 5.0) 624.92 0.05))
  (test with_overtime (near? (net_pay 50.0 20.0 10.0 5.0) 859.27 0.05))
  (test zero_hours (near? (net_pay 0.0 20.0 10.0 5.0) 0.0 0.01))
  (test no_tax (near? (net_pay 40.0 10.0 0.0 0.0) 369.4 0.05)))""")

add("multi", "rental_cost_weekly_loyalty_tax",
    "Rental cost: every 7 days costs 6×daily, remainder at daily; +insurance per actual day; if loyalty=1 → 10% off; +tax_pct%. Round to 2 decimals.",
    """(module Rental
  (defun round2 (x)
    (/ (math.floor (+ (* x 100.0) 0.5)) 100.0))
  (defun base_days (days daily)
    (def weeks (math.floor (/ days 7.0)))
    (def rem (- days (* weeks 7.0)))
    (+ (* weeks (* 6.0 daily)) (* rem daily)))
  (defun rental_cost (days daily insurance loyalty tax_pct)
    (def base (base_days days daily))
    (def with_ins (+ base (* days insurance)))
    (def discounted (if (= loyalty 1) (* with_ins 0.9) with_ins))
    (def taxed (* discounted (+ 1.0 (/ tax_pct 100.0))))
    (round2 taxed))
  (test one_day (near? (rental_cost 1.0 50.0 10.0 0 0.0) 60.0 0.01))
  (test week (near? (rental_cost 7.0 50.0 10.0 0 0.0) 370.0 0.01))
  (test eight_days (near? (rental_cost 8.0 50.0 10.0 0 0.0) 430.0 0.01))
  (test loyalty (near? (rental_cost 7.0 50.0 10.0 1 0.0) 333.0 0.01))
  (test with_tax (near? (rental_cost 7.0 50.0 10.0 0 10.0) 407.0 0.01))
  (test loyalty_tax (near? (rental_cost 7.0 50.0 10.0 1 10.0) 366.3 0.01)))""")

add("multi", "shipping_cost_zone_weight_express",
    "Shipping cost: 5 base + 0.5 per lb + zone fee [0,2,5,10] indexed by zone (0-3) + 15 if express (1/0). Round to 2 decimals.",
    """(module Shipping
  (defun round2 (x)
    (/ (math.floor (+ (* x 100.0) 0.5)) 100.0))
  (defun zone_fee (z)
    (if (= z 0) 0.0
      (if (= z 1) 2.0
        (if (= z 2) 5.0
          (if (= z 3) 10.0 0.0)))))
  (defun shipping_cost (weight_lbs zone express)
    (def base 5.0)
    (def w (* weight_lbs 0.5))
    (def z (zone_fee zone))
    (def e (if (= express 1) 15.0 0.0))
    (round2 (+ (+ (+ base w) z) e)))
  (test minimal (near? (shipping_cost 0.0 0 0) 5.0 0.01))
  (test zone_only (near? (shipping_cost 0.0 2 0) 10.0 0.01))
  (test weighted (near? (shipping_cost 4.0 0 0) 7.0 0.01))
  (test express (near? (shipping_cost 0.0 0 1) 20.0 0.01))
  (test combined (near? (shipping_cost 10.0 3 1) 35.0 0.01))
  (test fractional (near? (shipping_cost 2.5 1 0) 8.25 0.01)))""")

# ============== math.min / math.max anchor ==============

add("pure", "clamp_with_math_min_max",
    "Clamp a value into the range [lo, hi] using math.min and math.max.",
    """(module ClampMM
  (defun clamp (x lo hi)
    (math.min hi (math.max lo x)))
  (test in (eq? (clamp 5 0 10) 5))
  (test below (eq? (clamp -3 0 10) 0))
  (test above (eq? (clamp 99 0 10) 10))
  (test on_lo (eq? (clamp 0 0 10) 0))
  (test on_hi (eq? (clamp 10 0 10) 10)))""")

add("pure", "min_max_pair_with_math_funcs",
    "Return min(a,b) + max(a,b) (always equals a+b).",
    """(module MinMaxSum
  (defun mmsum (a b)
    (+ (math.min a b) (math.max a b)))
  (test ascending (eq? (mmsum 3 7) 10))
  (test descending (eq? (mmsum 7 3) 10))
  (test equal (eq? (mmsum 5 5) 10))
  (test neg (eq? (mmsum -2 4) 2)))""")

# ============== Array boundary cases ==============

add("pure", "first_or_default_array",
    "Return first element or `default` if array length is 0.",
    """(module FirstOr
  (defun first_or (a n default)
    (if (= n 0) (return default))
    (arr.get a 0))
  (test empty (do
    (def a : (Array Num) (arr.new 0))
    (assert-eq (first_or a 0 -1) -1)))
  (test single (do
    (def a : (Array Num) (arr.new 1))
    (arr.set a 0 99)
    (assert-eq (first_or a 1 -1) 99))))""")

add("pure", "average_or_zero_empty",
    "Average of array; return 0.0 if length 0.",
    """(module AvgOr0
  (defun avg_or_zero (a n)
    (if (= n 0) (return 0.0))
    (def s 0.0)
    (def i 0)
    (while (< i n) (do (set s (+ s (arr.get a i))) (set i (+ i 1))))
    (/ s n))
  (test empty (do
    (def a : (Array Num) (arr.new 0))
    (assert-near (avg_or_zero a 0) 0.0 0.0001)))
  (test three (do
    (def a : (Array Num) (arr.new 3))
    (arr.set a 0 2.0) (arr.set a 1 4.0) (arr.set a 2 6.0)
    (assert-near (avg_or_zero a 3) 4.0 0.0001))))""")

add("pure", "single_element_min_eq_max",
    "Min and max of a single-element array are equal to that element.",
    """(module SingleMinMax
  (defun fmin (a n)
    (def m (arr.get a 0))
    (def i 1)
    (while (< i n)
      (do (if (< (arr.get a i) m) (set m (arr.get a i)) 0) (set i (+ i 1))))
    m)
  (defun fmax (a n)
    (def m (arr.get a 0))
    (def i 1)
    (while (< i n)
      (do (if (> (arr.get a i) m) (set m (arr.get a i)) 0) (set i (+ i 1))))
    m)
  (test t (do
    (def a : (Array Num) (arr.new 1))
    (arr.set a 0 42)
    (assert-eq (fmin a 1) 42)
    (assert-eq (fmax a 1) 42))))""")

# ============== Threshold-boundary predicates ==============

add("pure", "score_to_grade_with_boundaries",
    "Map score to grade. Boundaries are inclusive at the lower edge: 90→A, 80→B, 70→C, 60→D, else F.",
    """(module Grade
  (defun grade (s)
    (if (>= s 90.0) (return "A"))
    (if (>= s 80.0) (return "B"))
    (if (>= s 70.0) (return "C"))
    (if (>= s 60.0) (return "D"))
    "F")
  (test exact_a (eq? (grade 90.0) "A"))
  (test exact_b (eq? (grade 80.0) "B"))
  (test exact_c (eq? (grade 70.0) "C"))
  (test exact_d (eq? (grade 60.0) "D"))
  (test below_d (eq? (grade 59.99) "F"))
  (test high (eq? (grade 100.0) "A")))""")

add("pure", "fee_with_two_thresholds",
    "Tier fee: <100 → flat 5, 100-499 → 5%, >=500 → flat 25 + 3% of (amount-500).",
    """(module FeeTier
  (defun round2 (x)
    (/ (math.floor (+ (* x 100.0) 0.5)) 100.0))
  (defun fee (amount)
    (if (< amount 100.0) (return 5.0))
    (if (< amount 500.0) (return (round2 (* amount 0.05))))
    (round2 (+ 25.0 (* (- amount 500.0) 0.03))))
  (test low (near? (fee 50.0) 5.0 0.01))
  (test mid (near? (fee 200.0) 10.0 0.01))
  (test boundary (near? (fee 500.0) 25.0 0.01))
  (test high (near? (fee 1000.0) 40.0 0.01)))""")


def main() -> None:
    out = sys.stdout
    for cat, topic, obj, sol in PAIRS:
        out.write(json.dumps({
            "category": cat,
            "topic": topic,
            "objective": obj,
            "solution": sol,
        }) + "\n")
    sys.stderr.write(f"Wrote {len(PAIRS)} pairs.\n")


if __name__ == "__main__":
    main()
