#!/usr/bin/env python3
"""Batch v4: idiom-anchor pairs.

Each pair is a small realistic task whose CANONICAL solution embeds one or two
syntax idioms learned during corpus authoring. Objectives are written task-side
(no meta-commentary about syntax) so the fine-tuned model learns idioms by
imitation, not by prescription.

Idioms anchored:
  1. eq?/near? are assert forms at test top-level only; inside expressions
     use = for strings/nums or str_eq.
  2. near? requires 3 args (actual, expected, eps).
  3. No (let ((var val)) body) — use (def name [: T] value) + (set ...).
  4. (while cond (do body...)) needs (do ...) for multi-statement bodies.
  5. (if cond then else) — the else is required (use 0 as no-op).
  6. No [1 2 3] literals — use arr.new + arr.set; for (Array Str) use str.split
     or map.keys.
  7. (and a b) does NOT short-circuit — guard with nested if instead.
  8. arr.length / str.substring / str.split are canonical names.
  9. Capability call sites need (extern defun fn (args) @capability "cap.name");
     test mocking is (test t (mocks (cap.name "arg" "result")) ...).
"""
from __future__ import annotations
import json
import sys


PAIRS: list[tuple[str, str, str, str]] = []


def add(category: str, topic: str, objective: str, solution: str) -> None:
    PAIRS.append((category, topic, objective, solution.strip()))


# ============== 1. String equality in expressions ==============

add("pure", "password_match",
    "Return 1 if the input matches the password 'secret', else 0.",
    """(module PwCheck
  (defun check (s)
    (if (= s "secret") 1 0))
  (test ok (eq? (check "secret") 1))
  (test wrong (eq? (check "guess") 0))
  (test empty (eq? (check "") 0)))""")

add("pure", "country_to_continent",
    "Map a country code to its continent: 'US'->'NA', 'BR'->'SA', 'FR'->'EU', else 'OTHER'.",
    """(module Continent
  (defun cont (code)
    (if (= code "US") "NA"
      (if (= code "BR") "SA"
        (if (= code "FR") "EU" "OTHER"))))
  (test us (eq? (cont "US") "NA"))
  (test br (eq? (cont "BR") "SA"))
  (test fr (eq? (cont "FR") "EU"))
  (test xx (eq? (cont "ZZ") "OTHER")))""")

add("pure", "string_equality_via_str_eq",
    "Return the count of how many of three strings equal a target.",
    """(module CountMatches
  (defun cm (target a b c)
    (def k 0)
    (if (str_eq target a) (set k (+ k 1)) 0)
    (if (str_eq target b) (set k (+ k 1)) 0)
    (if (str_eq target c) (set k (+ k 1)) 0)
    k)
  (test all (eq? (cm "x" "x" "x" "x") 3))
  (test one (eq? (cm "x" "x" "y" "z") 1))
  (test none (eq? (cm "x" "a" "b" "c") 0)))""")

add("pure", "ci_string_equal",
    "Case-insensitive equality between two strings (return 1 or 0).",
    """(module CIEq
  (defun ieq (a b)
    (if (= (str_lower a) (str_lower b)) 1 0))
  (test exact (eq? (ieq "Hello" "hello") 1))
  (test mixed (eq? (ieq "ABC" "AbC") 1))
  (test diff (eq? (ieq "hi" "bye") 0)))""")

# ============== 2. Float comparison with epsilon ==============

add("pure", "float_within_epsilon",
    "Return 1 if |a-b| <= eps, else 0 (a tolerance check).",
    """(module FloatClose
  (defun close (a b eps)
    (if (<= (math.abs (- a b)) eps) 1 0))
  (test exact (eq? (close 1.0 1.0 0.001) 1))
  (test within (eq? (close 1.0 1.0005 0.001) 1))
  (test outside (eq? (close 1.0 2.0 0.001) 0)))""")

add("pure", "average_float_with_near_assert",
    "Average of three floats; assertions tolerate 1e-4 epsilon.",
    """(module AvgFloat
  (defun avg3 (a b c) (/ (+ (+ a b) c) 3.0))
  (test eq (near? (avg3 1.0 1.0 1.0) 1.0 0.0001))
  (test mix (near? (avg3 2.0 4.0 6.0) 4.0 0.0001))
  (test neg (near? (avg3 -1.0 0.0 1.0) 0.0 0.0001)))""")

# ============== 3. Locals with def + set (no let) ==============

add("pure", "running_total_with_def_and_set",
    "Sum the integers from 1 to n inclusive (loop, accumulator).",
    """(module RunTotal
  (defun rsum (n)
    (def total 0)
    (def i 1)
    (while (<= i n)
      (do
        (set total (+ total i))
        (set i (+ i 1))))
    total)
  (test n0 (eq? (rsum 0) 0))
  (test n5 (eq? (rsum 5) 15))
  (test n100 (eq? (rsum 100) 5050)))""")

add("pure", "two_locals_swap_via_temp",
    "Swap two values via a temporary local; return them as 'a,b' string.",
    """(module SwapPair
  (defun swap_show (a b)
    (def x a)
    (def y b)
    (def t x)
    (set x y)
    (set y t)
    (str_concat (str_concat (str.from_num x) ",") (str.from_num y)))
  (test simple (eq? (swap_show 3 7) "7,3"))
  (test same (eq? (swap_show 5 5) "5,5"))
  (test neg (eq? (swap_show -1 2) "2,-1")))""")

# ============== 4. While with explicit (do …) ==============

add("pure", "geometric_progression_count",
    "Count how many times you can double 1 before exceeding n (i.e., floor(log2(n))+1 for n>=1).",
    """(module GpCount
  (defun gp (n)
    (def x 1)
    (def k 0)
    (while (<= x n)
      (do
        (set x (* x 2))
        (set k (+ k 1))))
    k)
  (test n1 (eq? (gp 1) 1))
  (test n8 (eq? (gp 8) 4))
  (test n100 (eq? (gp 100) 7)))""")

# ============== 5. Early-return pattern ==============

add("pure", "early_return_first_negative",
    "Return the first negative element, or 0 if none.",
    """(module FirstNeg
  (defun first_neg (a n)
    (def i 0)
    (while (< i n)
      (do
        (if (< (arr.get a i) 0) (return (arr.get a i)) 0)
        (set i (+ i 1))))
    0)
  (test t (do
    (def a : (Array Num) (arr.new 5))
    (arr.set a 0 3) (arr.set a 1 5) (arr.set a 2 -2) (arr.set a 3 7) (arr.set a 4 -9)
    (assert-eq (first_neg a 5) -2)))
  (test none (do
    (def a : (Array Num) (arr.new 3))
    (arr.set a 0 1) (arr.set a 1 2) (arr.set a 2 3)
    (assert-eq (first_neg a 3) 0))))""")

add("pure", "early_return_short_input",
    "Return -1 immediately if string length < 3, else return the third character's position (always 2).",
    """(module ThirdPos
  (defun pos3 (s)
    (if (< (str_length s) 3) (return -1))
    2)
  (test long (eq? (pos3 "hello") 2))
  (test exact (eq? (pos3 "abc") 2))
  (test short (eq? (pos3 "hi") -1))
  (test empty (eq? (pos3 "") -1)))""")

# ============== 6. Recursion with base case first ==============

add("pure", "rec_factorial_base_first",
    "Compute n! recursively with n<=1 as the base case.",
    """(module RecFact
  (defun rfact (n)
    (if (<= n 1) (return 1))
    (* n (rfact (- n 1))))
  (test f0 (eq? (rfact 0) 1))
  (test f1 (eq? (rfact 1) 1))
  (test f5 (eq? (rfact 5) 120))
  (test f7 (eq? (rfact 7) 5040)))""")

add("pure", "rec_count_chars_recursive",
    "Recursively count occurrences of a single char in a string.",
    """(module RecCharCount
  (defun rcc (s c i n)
    (if (= i n) (return 0))
    (def rest (rcc s c (+ i 1) n))
    (if (= (str_substring s i 1) c) (+ rest 1) rest))
  (defun count_c (s c) (rcc s c 0 (str_length s)))
  (test mid (eq? (count_c "banana" "a") 3))
  (test miss (eq? (count_c "abc" "z") 0))
  (test empty (eq? (count_c "" "a") 0)))""")

# ============== 7. Loop-with-done-flag (no break statement) ==============

add("pure", "first_index_via_done_flag",
    "Return the first index where the value is greater than threshold; -1 if none.",
    """(module FirstAbove
  (defun first_above (a n t)
    (def i 0)
    (def done 0)
    (def r -1)
    (while (= done 0)
      (do
        (if (>= i n) (set done 1)
          (if (> (arr.get a i) t)
            (do (set r i) (set done 1))
            (set i (+ i 1))))))
    r)
  (test mid (do
    (def a : (Array Num) (arr.new 5))
    (arr.set a 0 1) (arr.set a 1 3) (arr.set a 2 7) (arr.set a 3 5) (arr.set a 4 9)
    (assert-eq (first_above a 5 4) 2)))
  (test miss (do
    (def a : (Array Num) (arr.new 3))
    (arr.set a 0 1) (arr.set a 1 2) (arr.set a 2 3)
    (assert-eq (first_above a 3 100) -1))))""")

# ============== 8. Guarded if instead of and-short-circuit ==============

add("pure", "guarded_index_lookup",
    "Sum array entries while index is in range AND value is positive (without relying on and short-circuit).",
    """(module GuardedSum
  (defun gsum (a n)
    (def s 0)
    (def i 0)
    (def keep 1)
    (while (= keep 1)
      (do
        (if (>= i n) (set keep 0)
          (if (<= (arr.get a i) 0) (set keep 0)
            (do
              (set s (+ s (arr.get a i)))
              (set i (+ i 1)))))))
    s)
  (test t (do
    (def a : (Array Num) (arr.new 5))
    (arr.set a 0 1) (arr.set a 1 2) (arr.set a 2 3) (arr.set a 3 -1) (arr.set a 4 99)
    (assert-eq (gsum a 5) 6))))""")

# ============== 9. Array creation: arr.new + arr.set canonical ==============

add("pure", "build_array_then_sum",
    "Build an array of [10, 20, 30, 40, 50] and return its sum.",
    """(module BuildSum
  (defun build_and_sum ()
    (def a : (Array Num) (arr.new 5))
    (arr.set a 0 10)
    (arr.set a 1 20)
    (arr.set a 2 30)
    (arr.set a 3 40)
    (arr.set a 4 50)
    (def s 0)
    (def i 0)
    (while (< i 5)
      (do
        (set s (+ s (arr.get a i)))
        (set i (+ i 1))))
    s)
  (test t (eq? (build_and_sum) 150)))""")

add("pure", "fill_array_with_squares",
    "Return an array of length n where element i = i*i; check sum.",
    """(module FillSquares
  (defun fill (n)
    (def a : (Array Num) (arr.new n))
    (def i 0)
    (while (< i n)
      (do
        (arr.set a i (* i i))
        (set i (+ i 1))))
    a)
  (test t (do
    (def a : (Array Num) (fill 5))
    (assert-eq (arr.get a 0) 0)
    (assert-eq (arr.get a 4) 16))))""")

# ============== 10. String arrays must come from str.split / map.keys ==============

add("pure", "split_then_join_first_two",
    "Split a CSV by comma, join the first two parts with a dash.",
    """(module SplitJoin
  (defun first_two (s)
    (def parts : (Array Str) (str_split s ","))
    (str_concat (str_concat (arr.get parts 0) "-") (arr.get parts 1)))
  (test simple (eq? (first_two "a,b,c") "a-b"))
  (test two (eq? (first_two "x,y") "x-y"))
  (test trailing (eq? (first_two "a,b,") "a-b")))""")

add("pure", "csv_count_via_split",
    "Count the comma-separated fields in a CSV row.",
    """(module CsvCount
  (defun fields (s)
    (if (str_eq s "") (return 0))
    (arr.length (str_split s ",")))
  (test three (eq? (fields "a,b,c") 3))
  (test one (eq? (fields "only") 1))
  (test empty (eq? (fields "") 0)))""")

# ============== 11. Maps: create + set + get + has ==============

add("pure", "map_basic_set_then_get",
    "Insert two key-value pairs into a map and return the second value.",
    """(module MapBasic
  (defun set_two_get_b ()
    (def m : (Map Str Num) (map.new))
    (map.set m "a" 1)
    (map.set m "b" 2)
    (map.get m "b"))
  (test t (eq? (set_two_get_b) 2)))""")

add("pure", "map_increment_or_init",
    "Increment a counter under key k, initializing it to 1 if missing.",
    """(module MapIncOrInit
  (defun bump (m k)
    (if (map.has m k)
      (map.set m k (+ (map.get m k) 1))
      (map.set m k 1))
    m)
  (test t (do
    (def m : (Map Str Num) (map.new))
    (bump m "x")
    (bump m "x")
    (bump m "y")
    (assert-eq (map.get m "x") 2)
    (assert-eq (map.get m "y") 1))))""")

add("pure", "map_iterate_via_keys",
    "Sum all values in a Map<Str,Num> by iterating map.keys.",
    """(module MapSum
  (defun mapsum (m)
    (def keys : (Array Str) (map.keys m))
    (def n (arr.length keys))
    (def s 0)
    (def i 0)
    (while (< i n)
      (do
        (set s (+ s (map.get m (arr.get keys i))))
        (set i (+ i 1))))
    s)
  (test t (do
    (def m : (Map Str Num) (map.new))
    (map.set m "a" 5)
    (map.set m "b" 10)
    (map.set m "c" 15)
    (assert-eq (mapsum m) 30))))""")

# ============== 12. Higher-order: arr.map / arr.filter / arr.reduce ==============

add("pure", "hof_map_named_fn_required",
    "Cube every element via arr.map, then sum via arr.reduce.",
    """(module CubeSum
  (defun cube (x) (* x (* x x)))
  (defun add2 (a b) (+ a b))
  (defun cube_sum (a)
    (def cubed : (Array Num) (arr.map a cube))
    (arr.reduce cubed add2 0))
  (test t (do
    (def a : (Array Num) (arr.new 3))
    (arr.set a 0 1) (arr.set a 1 2) (arr.set a 2 3)
    (assert-eq (cube_sum a) 36))))""")

add("pure", "hof_filter_predicate_returns_int",
    "Keep only positive numbers via arr.filter; predicate returns 1 or 0.",
    """(module KeepPositive
  (defun pos (x) (if (> x 0) 1 0))
  (defun count_pos (a)
    (def kept : (Array Num) (arr.filter a pos))
    (arr.length kept))
  (test t (do
    (def a : (Array Num) (arr.new 5))
    (arr.set a 0 -1) (arr.set a 1 0) (arr.set a 2 3) (arr.set a 3 -7) (arr.set a 4 9)
    (assert-eq (count_pos a) 2))))""")

add("pure", "hof_full_pipeline_clean",
    "Pipeline: square, keep > 4, sum.",
    """(module FullPipe
  (defun sq (x) (* x x))
  (defun gt4 (x) (if (> x 4) 1 0))
  (defun add2 (a b) (+ a b))
  (defun pipeline (a)
    (def s : (Array Num) (arr.map a sq))
    (def f : (Array Num) (arr.filter s gt4))
    (arr.reduce f add2 0))
  (test t (do
    (def a : (Array Num) (arr.new 4))
    (arr.set a 0 1) (arr.set a 1 2) (arr.set a 2 3) (arr.set a 3 4)
    (assert-eq (pipeline a) 25))))""")

# ============== 13. Capability extern + mocks ==============

add("cap", "cap_extern_minimal_shape",
    "Read an env var and return it unchanged; default 'unset' if blank.",
    """(module EnvShape
  (extern defun env_read (k) @capability "env.get")
  (defun read_or (k)
    (def v (env_read k))
    (if (str_eq v "") (return "unset"))
    v)
  (test missing (mocks (env.get "NAME" "")) (eq? (read_or "NAME") "unset"))
  (test set (mocks (env.get "NAME" "alice")) (eq? (read_or "NAME") "alice")))""")

add("cap", "cap_two_capabilities_same_test",
    "Read 'PATH' from env, then read that file's content and return its length.",
    """(module EnvFileLen
  (extern defun env_read (k) @capability "env.get")
  (extern defun file_read (p) @capability "file.read")
  (defun lookup_len (k)
    (def path (env_read k))
    (str_length (file_read path)))
  (test t
    (mocks (env.get "P" "/tmp/hello.txt") (file.read "/tmp/hello.txt" "hello"))
    (eq? (lookup_len "P") 5))
  (test empty
    (mocks (env.get "P" "/tmp/empty") (file.read "/tmp/empty" ""))
    (eq? (lookup_len "P") 0)))""")

add("cap", "cap_http_classify_body",
    "Fetch a URL and classify the body as 'short' (<5 chars) or 'long'.",
    """(module HttpClassify
  (extern defun http_get (u) @capability "http.fetch")
  (defun classify (u)
    (if (< (str_length (http_get u)) 5) "short" "long"))
  (test small (mocks (http.fetch "http://x" "ok")) (eq? (classify "http://x") "short"))
  (test big (mocks (http.fetch "http://x" "this is a long body")) (eq? (classify "http://x") "long")))""")

# ============== 14. Multi-mock with same capability across multiple test arguments ==============

add("cap", "cap_same_cap_multi_keys",
    "Read three env vars and return their concatenation joined with ':'.",
    """(module EnvJoin3Cap
  (extern defun env_read (k) @capability "env.get")
  (defun join3 (a b c)
    (str_concat (str_concat (str_concat (str_concat (env_read a) ":") (env_read b)) ":") (env_read c)))
  (test t
    (mocks (env.get "X" "alpha") (env.get "Y" "beta") (env.get "Z" "gamma"))
    (eq? (join3 "X" "Y" "Z") "alpha:beta:gamma"))
  (test empty_mid
    (mocks (env.get "X" "a") (env.get "Y" "") (env.get "Z" "c"))
    (eq? (join3 "X" "Y" "Z") "a::c")))""")

# ============== 15. Contracts: require + ensure ==============

add("pure", "contract_require_positive_input",
    "Compute the square root of x with a require(x >= 0) precondition.",
    """(module SqrtSafe
  (defun ssqrt (x)
    (require (>= x 0.0))
    (math.sqrt x))
  (test zero (near? (ssqrt 0.0) 0.0 0.0001))
  (test four (near? (ssqrt 4.0) 2.0 0.0001))
  (test big (near? (ssqrt 100.0) 10.0 0.0001)))""")

add("pure", "contract_ensure_postcondition",
    "Compute |x| and ensure the result is non-negative.",
    """(module AbsEnsure
  (defun safe_abs (x)
    (def r (math.abs x))
    (ensure (>= r 0.0))
    r)
  (test pos (near? (safe_abs 3.0) 3.0 0.0001))
  (test neg (near? (safe_abs -7.0) 7.0 0.0001))
  (test zero (near? (safe_abs 0.0) 0.0 0.0001)))""")

# ============== 16. String building via concat in a loop ==============

add("pure", "build_string_in_loop",
    "Build a string of n copies of the digit character '5'.",
    """(module FiveString
  (defun fives (n)
    (def out "")
    (def i 0)
    (while (< i n)
      (do
        (set out (str_concat out "5"))
        (set i (+ i 1))))
    out)
  (test n0 (eq? (fives 0) ""))
  (test n3 (eq? (fives 3) "555"))
  (test n6 (eq? (fives 6) "555555")))""")

add("pure", "join_array_with_comma",
    "Join all integers in an array as a comma-separated string.",
    """(module JoinComma
  (defun joinc (a n)
    (def out "")
    (def i 0)
    (while (< i n)
      (do
        (if (= i 0)
          (set out (str.from_num (arr.get a i)))
          (set out (str_concat (str_concat out ",") (str.from_num (arr.get a i)))))
        (set i (+ i 1))))
    out)
  (test t (do
    (def a : (Array Num) (arr.new 4))
    (arr.set a 0 1) (arr.set a 1 2) (arr.set a 2 3) (arr.set a 3 4)
    (assert-eq (joinc a 4) "1,2,3,4"))))""")

# ============== 17. Substring window via str.substring(s, start, len) ==============

add("pure", "substring_window_canonical",
    "Return a length-2 sliding window starting at index i.",
    """(module Window2
  (defun w2 (s i)
    (str_substring s i 2))
  (test start (eq? (w2 "hello" 0) "he"))
  (test mid (eq? (w2 "hello" 2) "ll"))
  (test end (eq? (w2 "hello" 3) "lo")))""")

# ============== 18. Asserting truthiness directly via assert-true ==============

add("pure", "assert_true_for_predicate",
    "Predicate that returns 1 for primes <= 10 (2, 3, 5, 7), 0 otherwise.",
    """(module SmallPrime
  (defun is_small_prime (n)
    (if (= n 2) (return 1))
    (if (= n 3) (return 1))
    (if (= n 5) (return 1))
    (if (= n 7) (return 1))
    0)
  (test two (assert-true (= (is_small_prime 2) 1)))
  (test five (assert-true (= (is_small_prime 5) 1)))
  (test four (assert-true (= (is_small_prime 4) 0))))""")


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
