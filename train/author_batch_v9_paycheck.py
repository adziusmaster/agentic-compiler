#!/usr/bin/env python3
"""Batch v10 — paycheck/net-pay reinforcement anchors.

Bug observed on bench problem 22: model uses (round2 ...) but never defines it,
and writes (math.add x y) which doesn't exist (use + instead). It also gets
the federal-tax base wrong: federal applies to (gross-FICA), state to gross.

Every pair below:
  - Defines round2 inside the module
  - Uses + for addition (never math.add)
  - Splits FICA, federal, state correctly
"""
from __future__ import annotations
import json
import sys


PAIRS: list[tuple[str, str, str, str]] = []


def add(category: str, topic: str, objective: str, solution: str) -> None:
    PAIRS.append((category, topic, objective, solution.strip()))


add("multi", "net_pay_canonical_taxes",
    "Compute net pay: regular×rate + overtime (>40h at 1.5×) − FICA (7.65% of gross) − federal_pct of (gross−FICA) − state_pct of gross. Round to 2dp.",
    """(module NetPay
  (defun round2 (x) (/ (math.floor (+ (* x 100.0) 0.5)) 100.0))
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

add("multi", "salary_net_simple_three_taxes",
    "Salary minus three sequential tax deductions (each pct of gross). Round to 2dp.",
    """(module SalaryNet
  (defun round2 (x) (/ (math.floor (+ (* x 100.0) 0.5)) 100.0))
  (defun net (gross fed_pct state_pct fica_pct)
    (def fed (* gross (/ fed_pct 100.0)))
    (def state (* gross (/ state_pct 100.0)))
    (def fica (* gross (/ fica_pct 100.0)))
    (round2 (- (- (- gross fed) state) fica)))
  (test all_zero (near? (net 1000.0 0.0 0.0 0.0) 1000.0 0.01))
  (test only_fed (near? (net 1000.0 20.0 0.0 0.0) 800.0 0.01))
  (test all_three (near? (net 1000.0 10.0 5.0 7.65) 773.5 0.01)))""")

add("multi", "wage_overtime_round2",
    "Compute gross wage with overtime past 40h at 1.5×. Define round2 explicitly. Round result.",
    """(module WageOT
  (defun round2 (x) (/ (math.floor (+ (* x 100.0) 0.5)) 100.0))
  (defun wage (hours rate)
    (def reg (* (math.min hours 40.0) rate))
    (def ot (* (math.max 0.0 (- hours 40.0)) (* rate 1.5)))
    (round2 (+ reg ot)))
  (test reg (near? (wage 40.0 20.0) 800.0 0.01))
  (test ot (near? (wage 50.0 20.0) 1100.0 0.01))
  (test zero (near? (wage 0.0 20.0) 0.0 0.01))
  (test exact_50 (near? (wage 50.0 10.0) 550.0 0.01)))""")

add("multi", "fica_only_paycheck",
    "Net = gross − FICA (7.65%). Define round2 helper. No other taxes.",
    """(module FicaOnly
  (defun round2 (x) (/ (math.floor (+ (* x 100.0) 0.5)) 100.0))
  (defun gross_pay (hours rate)
    (def reg (* (math.min hours 40.0) rate))
    (def ot (* (math.max 0.0 (- hours 40.0)) (* rate 1.5)))
    (+ reg ot))
  (defun net_fica (hours rate)
    (def g (gross_pay hours rate))
    (round2 (- g (* g 0.0765))))
  (test no_ot (near? (net_fica 40.0 10.0) 369.4 0.05))
  (test with_ot (near? (net_fica 50.0 10.0) 507.93 0.05))
  (test zero (near? (net_fica 0.0 10.0) 0.0 0.01)))""")

add("multi", "tax_on_post_fica_base",
    "Federal tax applies to (gross − FICA), state to gross. FICA = 7.65%. Round to 2dp.",
    """(module TaxBases
  (defun round2 (x) (/ (math.floor (+ (* x 100.0) 0.5)) 100.0))
  (defun deductions (gross fed_pct state_pct)
    (def fica (* gross 0.0765))
    (def fed (* (- gross fica) (/ fed_pct 100.0)))
    (def state (* gross (/ state_pct 100.0)))
    (round2 (+ (+ fica fed) state)))
  (test no_taxes (near? (deductions 1000.0 0.0 0.0) 76.5 0.05))
  (test fed_only (near? (deductions 1000.0 10.0 0.0) 168.85 0.05))
  (test all_three (near? (deductions 1000.0 10.0 5.0) 218.85 0.05)))""")

add("multi", "freelance_net_round",
    "Freelance net: hours×rate − fica_pct% of gross. Round.",
    """(module FreelanceNet
  (defun round2 (x) (/ (math.floor (+ (* x 100.0) 0.5)) 100.0))
  (defun fl_net (hours rate fica_pct)
    (def gross (* hours rate))
    (round2 (- gross (* gross (/ fica_pct 100.0)))))
  (test no_fica (near? (fl_net 10.0 50.0 0.0) 500.0 0.01))
  (test with_fica (near? (fl_net 20.0 100.0 7.65) 1847.0 0.01))
  (test zero_hours (near? (fl_net 0.0 50.0 7.65) 0.0 0.01)))""")

add("multi", "step_subtract_three_round2",
    "Subtract three values from a base using nested (- ...) calls; round result.",
    """(module SubThree
  (defun round2 (x) (/ (math.floor (+ (* x 100.0) 0.5)) 100.0))
  (defun sub3 (base a b c)
    (round2 (- (- (- base a) b) c)))
  (test all_zero (near? (sub3 100.0 0.0 0.0 0.0) 100.0 0.01))
  (test typical (near? (sub3 1000.0 50.0 30.0 20.0) 900.0 0.01))
  (test all (near? (sub3 500.0 100.0 100.0 100.0) 200.0 0.01)))""")

add("multi", "compute_overtime_components",
    "Return regular pay and overtime pay separately, then sum into gross. Round result.",
    """(module OTComponents
  (defun round2 (x) (/ (math.floor (+ (* x 100.0) 0.5)) 100.0))
  (defun reg_part (hours rate)
    (* (math.min hours 40.0) rate))
  (defun ot_part (hours rate)
    (* (math.max 0.0 (- hours 40.0)) (* rate 1.5)))
  (defun gross_pay (hours rate)
    (round2 (+ (reg_part hours rate) (ot_part hours rate))))
  (test reg_only (near? (gross_pay 30.0 20.0) 600.0 0.01))
  (test exact_40 (near? (gross_pay 40.0 20.0) 800.0 0.01))
  (test plus_5 (near? (gross_pay 45.0 20.0) 950.0 0.01))
  (test zero (near? (gross_pay 0.0 20.0) 0.0 0.01)))""")


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
