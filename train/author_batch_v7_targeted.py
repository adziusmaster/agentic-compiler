#!/usr/bin/env python3
"""Batch v7 targeted: pattern-anchor pairs for the 6 still-stuck bench problems.

Each subgroup attacks one specific failure mode discovered during eval:
  - 23 (rental cost): model skips weekly-discount logic
  - 26 (progressive tax): model uses bare `min`/`max` not `math.min`/`math.max`
  - 17 (line count): trailing-no-newline edge case under-represented
  - 22 (paycheck net): multi-rule conditional pipelines
  - 28 (shipping cost): compound-fee with table lookup
  - 21 (invoice): zero-quantity edge case in multi-step pipelines

These are PATTERN ANCHORS not test answers — same shape as bench problems
but different specific numbers / semantics.
"""
from __future__ import annotations
import json
import sys


PAIRS: list[tuple[str, str, str, str]] = []


def add(category: str, topic: str, objective: str, solution: str) -> None:
    PAIRS.append((category, topic, objective, solution.strip()))


# ============== Period-pricing (anchors 23-rental-cost) ==============
# Pattern: every N units gets a discount, remainder at base rate.

add("multi", "weekly_subscription_pricing",
    "Subscription cost: every 7-day block costs 6×daily_rate (one day free); remainder days at full rate.",
    """(module WeekSub
  (defun period_cost (days daily)
    (def weeks (math.floor (/ days 7.0)))
    (def rem (- days (* weeks 7.0)))
    (+ (* weeks (* 6.0 daily)) (* rem daily)))
  (test exact_week (near? (period_cost 7.0 10.0) 60.0 0.001))
  (test eight_days (near? (period_cost 8.0 10.0) 70.0 0.001))
  (test fourteen (near? (period_cost 14.0 10.0) 120.0 0.001))
  (test six_days (near? (period_cost 6.0 10.0) 60.0 0.001))
  (test fifteen (near? (period_cost 15.0 10.0) 130.0 0.001)))""")

add("multi", "monthly_block_discount",
    "Every 30-day block costs 25×daily; remainder at full daily.",
    """(module MonthBlock
  (defun stay_cost (days daily)
    (def months (math.floor (/ days 30.0)))
    (def rem (- days (* months 30.0)))
    (+ (* months (* 25.0 daily)) (* rem daily)))
  (test exact (near? (stay_cost 30.0 10.0) 250.0 0.001))
  (test plus_one (near? (stay_cost 31.0 10.0) 260.0 0.001))
  (test partial (near? (stay_cost 15.0 10.0) 150.0 0.001)))""")

add("multi", "rental_with_loyalty_and_tax",
    "Period pricing + per-day insurance, then loyalty 15% off if loyalty=1, then tax_pct% on top. Round to 2 decimals.",
    """(module RentalLT
  (defun round2 (x) (/ (math.floor (+ (* x 100.0) 0.5)) 100.0))
  (defun period_base (days daily)
    (def weeks (math.floor (/ days 7.0)))
    (def rem (- days (* weeks 7.0)))
    (+ (* weeks (* 6.0 daily)) (* rem daily)))
  (defun rental_lt (days daily insurance loyalty tax_pct)
    (def base (period_base days daily))
    (def with_ins (+ base (* days insurance)))
    (def discounted (if (= loyalty 1) (* with_ins 0.85) with_ins))
    (def taxed (* discounted (+ 1.0 (/ tax_pct 100.0))))
    (round2 taxed))
  (test plain (near? (rental_lt 7.0 50.0 10.0 0 0.0) 370.0 0.01))
  (test eight_days (near? (rental_lt 8.0 50.0 10.0 0 0.0) 430.0 0.01))
  (test loyalty_off (near? (rental_lt 7.0 50.0 10.0 1 0.0) 314.5 0.01))
  (test taxed (near? (rental_lt 7.0 50.0 10.0 0 10.0) 407.0 0.01)))""")

add("multi", "boat_rental_hourly_daily",
    "Hourly rental: every 8 hours rounds up to a day (12×hourly); remainder hours at hourly rate.",
    """(module BoatRent
  (defun rent (hours hourly)
    (def days (math.floor (/ hours 8.0)))
    (def rem (- hours (* days 8.0)))
    (+ (* days (* 12.0 hourly)) (* rem hourly)))
  (test under_day (near? (rent 5.0 10.0) 50.0 0.001))
  (test exact_day (near? (rent 8.0 10.0) 120.0 0.001))
  (test plus_one (near? (rent 9.0 10.0) 130.0 0.001))
  (test two_days (near? (rent 16.0 10.0) 240.0 0.001)))""")

# ============== Bracket-piecewise math with math.min/math.max ==============
# Anchors 26-tax-progressive. Every test forces math.min / math.max usage.

add("multi", "two_bracket_with_math_funcs",
    "Two-tier compute: 10% on first 5000, 20% on the rest. Use math.min and math.max only.",
    """(module TwoBracketMM
  (defun split_tax (x)
    (def low (* (math.min x 5000.0) 0.10))
    (def high (* (math.max 0.0 (- x 5000.0)) 0.20))
    (+ low high))
  (test zero (near? (split_tax 0.0) 0.0 0.01))
  (test under (near? (split_tax 3000.0) 300.0 0.01))
  (test boundary (near? (split_tax 5000.0) 500.0 0.01))
  (test over (near? (split_tax 10000.0) 1500.0 0.01)))""")

add("multi", "three_bracket_explicit",
    "Three-tier rates: 5% up to 1000, 10% on 1000–5000, 20% above 5000. Round to 2dp.",
    """(module ThreeBracket
  (defun round2 (x) (/ (math.floor (+ (* x 100.0) 0.5)) 100.0))
  (defun tier (x)
    (def t1 (* (math.min x 1000.0) 0.05))
    (def t2 (* (math.max 0.0 (- (math.min x 5000.0) 1000.0)) 0.10))
    (def t3 (* (math.max 0.0 (- x 5000.0)) 0.20))
    (round2 (+ (+ t1 t2) t3)))
  (test zero (near? (tier 0.0) 0.0 0.01))
  (test in_first (near? (tier 500.0) 25.0 0.01))
  (test boundary1 (near? (tier 1000.0) 50.0 0.01))
  (test in_second (near? (tier 3000.0) 250.0 0.01))
  (test boundary2 (near? (tier 5000.0) 450.0 0.01))
  (test in_third (near? (tier 10000.0) 1450.0 0.01)))""")

add("multi", "shipping_zone_tax",
    "Two-bracket shipping cost: 5/lb up to 10 lbs, then 8/lb for the rest. Use math.min/max.",
    """(module ShipBracket
  (defun cost (lbs)
    (def base (* (math.min lbs 10.0) 5.0))
    (def heavy (* (math.max 0.0 (- lbs 10.0)) 8.0))
    (+ base heavy))
  (test small (near? (cost 5.0) 25.0 0.01))
  (test boundary (near? (cost 10.0) 50.0 0.01))
  (test heavy (near? (cost 15.0) 90.0 0.01)))""")

add("multi", "discount_capped_with_min",
    "Discount = 10% of amount, capped at 100. Always use math.min.",
    """(module CappedDisc
  (defun disc (amount)
    (math.min (* amount 0.10) 100.0))
  (test small (near? (disc 200.0) 20.0 0.01))
  (test capped (near? (disc 5000.0) 100.0 0.01))
  (test exact (near? (disc 1000.0) 100.0 0.01))
  (test zero (near? (disc 0.0) 0.0 0.01)))""")

add("multi", "income_three_bracket_v2",
    "Progressive tax: 12% up to 8000, 22% on 8000–40000, 32% above 40000. Round to 2dp.",
    """(module IncomeBrackets
  (defun round2 (x) (/ (math.floor (+ (* x 100.0) 0.5)) 100.0))
  (defun income_pay (income)
    (def t1 (* (math.min income 8000.0) 0.12))
    (def t2 (* (math.max 0.0 (- (math.min income 40000.0) 8000.0)) 0.22))
    (def t3 (* (math.max 0.0 (- income 40000.0)) 0.32))
    (round2 (+ (+ t1 t2) t3)))
  (test zero (near? (income_pay 0.0) 0.0 0.01))
  (test low (near? (income_pay 5000.0) 600.0 0.01))
  (test mid (near? (income_pay 20000.0) 3600.0 0.01))
  (test boundary (near? (income_pay 40000.0) 8000.0 0.01))
  (test high (near? (income_pay 50000.0) 11200.0 0.01)))""")

add("multi", "max_with_floor_zero",
    "Floor a value at zero (return non-negative version) using math.max.",
    """(module FloorZero
  (defun nn (x) (math.max 0.0 x))
  (test pos (near? (nn 5.0) 5.0 0.001))
  (test neg (near? (nn -3.0) 0.0 0.001))
  (test zero (near? (nn 0.0) 0.0 0.001)))""")

# ============== Line counting variants (anchors 17) ==============

add("pure", "lines_with_trailing_text",
    "Count lines: each '\\n' starts a new line; trailing non-empty text without newline ALSO counts as a line.",
    """(module CountLinesT
  (defun count_lines (s)
    (def n (str_length s))
    (if (= n 0) (return 0))
    (def c 0)
    (def i 0)
    (while (< i n)
      (do
        (if (str_eq (str_substring s i 1) "\\n") (set c (+ c 1)) 0)
        (set i (+ i 1))))
    (def last_ch (str_substring s (- n 1) 1))
    (if (str_eq last_ch "\\n") c (+ c 1)))
  (test empty (eq? (count_lines "") 0))
  (test one_term (eq? (count_lines "a\\n") 1))
  (test two_term (eq? (count_lines "a\\nb\\n") 2))
  (test trailing_no_nl (eq? (count_lines "a\\nb") 2))
  (test bare_text (eq? (count_lines "hello") 1))
  (test only_nl (eq? (count_lines "\\n") 1))
  (test double_nl (eq? (count_lines "\\n\\n") 2))
  (test trailing_after_two (eq? (count_lines "a\\nb\\nc") 3)))""")

add("pure", "non_blank_line_count",
    "Count lines that contain at least one non-whitespace character.",
    """(module CountNonBlank
  (defun count_nb (s)
    (def n (str_length s))
    (if (= n 0) (return 0))
    (def c 0)
    (def i 0)
    (def has_content 0)
    (while (< i n)
      (do
        (def ch (str_substring s i 1))
        (if (str_eq ch "\\n")
          (do (if (= has_content 1) (set c (+ c 1)) 0) (set has_content 0))
          (if (str_eq ch " ") 0 (set has_content 1)))
        (set i (+ i 1))))
    (if (= has_content 1) (+ c 1) c))
  (test all_blank (eq? (count_nb "\\n\\n\\n") 0))
  (test one (eq? (count_nb "abc\\n") 1))
  (test mix (eq? (count_nb "a\\n\\nb\\n") 2))
  (test trailing (eq? (count_nb "a\\nb") 2)))""")

# ============== Multi-rule paycheck-style (anchors 22-paycheck-net) ==============

add("multi", "salary_with_overtime_and_bonus",
    "Pay = regular hours×rate + overtime (>40h at 1.5×) + flat bonus. Round to 2dp.",
    """(module PaySimple
  (defun round2 (x) (/ (math.floor (+ (* x 100.0) 0.5)) 100.0))
  (defun gross (hours rate bonus)
    (def reg (* (math.min hours 40.0) rate))
    (def ot (* (math.max 0.0 (- hours 40.0)) (* rate 1.5)))
    (round2 (+ (+ reg ot) bonus)))
  (test no_overtime (near? (gross 40.0 20.0 0.0) 800.0 0.01))
  (test with_overtime (near? (gross 50.0 20.0 0.0) 1100.0 0.01))
  (test zero_hours (near? (gross 0.0 20.0 50.0) 50.0 0.01))
  (test bonus_added (near? (gross 40.0 20.0 100.0) 900.0 0.01)))""")

add("multi", "consultant_billing",
    "Consultant gross = hours×rate; if hours=0 return 0; FICA=7.65%; net = gross-FICA. Round to 2dp.",
    """(module Consult
  (defun round2 (x) (/ (math.floor (+ (* x 100.0) 0.5)) 100.0))
  (defun net_consult (hours rate)
    (if (= hours 0.0) (return 0.0))
    (def gross (* hours rate))
    (def fica (* gross 0.0765))
    (round2 (- gross fica)))
  (test zero (near? (net_consult 0.0 100.0) 0.0 0.01))
  (test ten_hours (near? (net_consult 10.0 100.0) 923.5 0.01))
  (test small (near? (net_consult 1.0 50.0) 46.18 0.01)))""")

# ============== Compound-fee shipping (anchors 28) ==============

add("multi", "fee_with_zone_lookup",
    "Cost = base + per_unit*units + zone_fee[zone]; zone_fee table is 0,3,7,12 for zones 0..3.",
    """(module ZoneFee
  (defun zone_lookup (z)
    (if (= z 0) 0.0
      (if (= z 1) 3.0
        (if (= z 2) 7.0
          (if (= z 3) 12.0 0.0)))))
  (defun total (base per_unit units zone)
    (+ (+ base (* per_unit units)) (zone_lookup zone)))
  (test zone_0 (near? (total 5.0 0.5 10.0 0) 10.0 0.01))
  (test zone_2 (near? (total 5.0 0.5 10.0 2) 17.0 0.01))
  (test fractional (near? (total 5.0 0.5 2.5 1) 9.25 0.01))
  (test no_units (near? (total 5.0 0.5 0.0 3) 17.0 0.01)))""")

add("multi", "delivery_with_express_flag",
    "Delivery total: base + per_kg*kg + (express ? 10 : 0). Round to 2dp.",
    """(module Delivery
  (defun round2 (x) (/ (math.floor (+ (* x 100.0) 0.5)) 100.0))
  (defun delivery (kg express base per_kg)
    (def express_fee (if (= express 1) 10.0 0.0))
    (round2 (+ (+ base (* per_kg kg)) express_fee)))
  (test no_express (near? (delivery 2.0 0 5.0 1.5) 8.0 0.01))
  (test with_express (near? (delivery 2.0 1 5.0 1.5) 18.0 0.01))
  (test fractional (near? (delivery 2.5 0 5.0 1.5) 8.75 0.01))
  (test zero_kg (near? (delivery 0.0 0 5.0 1.5) 5.0 0.01)))""")

# ============== Invoice with zero-qty edge (anchors 21) ==============

add("multi", "invoice_zero_qty_safe",
    "Invoice: subtotal = price×qty (returns just shipping if qty=0), discount %, tax %, + shipping. Round to 2dp.",
    """(module InvoiceZQ
  (defun round2 (x) (/ (math.floor (+ (* x 100.0) 0.5)) 100.0))
  (defun invoice (price qty disc_pct tax_pct shipping)
    (def subtotal (* price qty))
    (def discounted (* subtotal (- 1.0 (/ disc_pct 100.0))))
    (def taxed (* discounted (+ 1.0 (/ tax_pct 100.0))))
    (round2 (+ taxed shipping)))
  (test base (near? (invoice 10.0 2.0 0.0 0.0 0.0) 20.0 0.01))
  (test zero_qty (near? (invoice 50.0 0.0 10.0 20.0 5.0) 5.0 0.01))
  (test full (near? (invoice 100.0 3.0 10.0 20.0 5.0) 329.0 0.01))
  (test shipping_only (near? (invoice 0.0 0.0 0.0 0.0 7.5) 7.5 0.01)))""")

add("multi", "order_with_optional_express",
    "Order: subtotal (price×qty) + tax + (express ? express_fee : 0). qty=0 → just express+tax of zero.",
    """(module OrderExp
  (defun round2 (x) (/ (math.floor (+ (* x 100.0) 0.5)) 100.0))
  (defun order_total (price qty tax_pct express express_fee)
    (def sub (* price qty))
    (def with_tax (* sub (+ 1.0 (/ tax_pct 100.0))))
    (def add (if (= express 1) express_fee 0.0))
    (round2 (+ with_tax add)))
  (test plain (near? (order_total 10.0 2.0 0.0 0 0.0) 20.0 0.01))
  (test express (near? (order_total 10.0 2.0 0.0 1 5.0) 25.0 0.01))
  (test zero_qty_express (near? (order_total 50.0 0.0 10.0 1 5.0) 5.0 0.01))
  (test taxed (near? (order_total 10.0 2.0 10.0 0 0.0) 22.0 0.01)))""")

# ============== Whitespace-aware word count variants (anchors 01) ==============

add("pure", "word_count_with_tabs",
    "Count whitespace-separated words; treat both ' ' and '\\t' as whitespace; collapse runs.",
    """(module WCTabs
  (defun is_space (c)
    (if (str_eq c " ") 1 (if (str_eq c "\\t") 1 0)))
  (defun wc (s)
    (def n (str_length s))
    (def c 0)
    (def i 0)
    (def in_word 0)
    (while (< i n)
      (do
        (def ch (str_substring s i 1))
        (if (= (is_space ch) 1)
          (set in_word 0)
          (do
            (if (= in_word 0) (set c (+ c 1)) 0)
            (set in_word 1)))
        (set i (+ i 1))))
    c)
  (test empty (eq? (wc "") 0))
  (test one (eq? (wc "hello") 1))
  (test two (eq? (wc "hello world") 2))
  (test leading (eq? (wc "  hi") 1))
  (test trailing (eq? (wc "hi  ") 1))
  (test collapse (eq? (wc "a  b   c") 3))
  (test tabs (eq? (wc "a\\tb\\tc") 3))
  (test mixed (eq? (wc "a \\tb") 2))
  (test only_ws (eq? (wc "   \\t  ") 0)))""")

add("pure", "split_then_count_nonempty",
    "Count non-empty whitespace-separated tokens after splitting by single space (no tab handling).",
    """(module CountNonEmpty
  (defun count_ne (s)
    (if (str_eq s "") (return 0))
    (def parts : (Array Str) (str_split s " "))
    (def n (arr.length parts))
    (def c 0)
    (def i 0)
    (while (< i n)
      (do
        (if (str_eq (arr.get parts i) "") 0 (set c (+ c 1)))
        (set i (+ i 1))))
    c)
  (test empty (eq? (count_ne "") 0))
  (test simple (eq? (count_ne "a b c") 3))
  (test leading (eq? (count_ne " a b") 2))
  (test trailing (eq? (count_ne "a b ") 2))
  (test multi_space (eq? (count_ne "a  b") 2)))""")


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
