#!/usr/bin/env python3
"""Batch v3: capability chains, HOF+recursion, multi-helper decomposition,
contracts, and string ops in canonical syntax. All Claude-authored AGC."""
from __future__ import annotations
import json
import sys


PAIRS: list[tuple[str, str, str, str]] = []


def add(category: str, topic: str, objective: str, solution: str) -> None:
    PAIRS.append((category, topic, objective, solution.strip()))


# ============== capability chains (multi-cap or longer reasoning) ==============

add("cap", "env_then_file_size_match",
    "Read a path from env, read its file content; return 1 if length matches a target env value, else 0.",
    """(module EnvFileMatch
  (extern defun env_read (k) @capability "env.get")
  (extern defun file_read (p) @capability "file.read")
  (defun match_size (path_key target_key)
    (def path (env_read path_key))
    (def expected (str_to_num (env_read target_key)))
    (def actual (str_length (file_read path)))
    (if (= actual expected) 1 0))
  (test ok (mocks (env.get "P" "/x") (env.get "N" "5") (file.read "/x" "hello")) (eq? (match_size "P" "N") 1))
  (test diff (mocks (env.get "P" "/x") (env.get "N" "5") (file.read "/x" "hi")) (eq? (match_size "P" "N") 0)))""")

add("cap", "file_then_http_compare_lengths",
    "Compare body length of an HTTP response to a file length; return 'equal'/'http-bigger'/'file-bigger'.",
    """(module CompareLen
  (extern defun http_get (u) @capability "http.fetch")
  (extern defun file_read (p) @capability "file.read")
  (defun cmp_len (url path)
    (def hl (str_length (http_get url)))
    (def fl (str_length (file_read path)))
    (if (= hl fl) "equal" (if (> hl fl) "http-bigger" "file-bigger")))
  (test eq (mocks (http.fetch "http://x" "abcde") (file.read "/p" "hello")) (eq? (cmp_len "http://x" "/p") "equal"))
  (test http_bigger (mocks (http.fetch "http://x" "abcdefgh") (file.read "/p" "ab")) (eq? (cmp_len "http://x" "/p") "http-bigger"))
  (test file_bigger (mocks (http.fetch "http://x" "ab") (file.read "/p" "abcdefgh")) (eq? (cmp_len "http://x" "/p") "file-bigger")))""")

add("cap", "env_path_then_file_word_count",
    "Read file path from env, return number of words in that file.",
    """(module EnvFileWords
  (extern defun env_read (k) @capability "env.get")
  (extern defun file_read (p) @capability "file.read")
  (defun wc_from_env (path_key)
    (def path (env_read path_key))
    (def content (file_read path))
    (if (str_eq content "") (return 0))
    (arr.length (str_split content " ")))
  (test three (mocks (env.get "P" "/notes") (file.read "/notes" "the cat sat")) (eq? (wc_from_env "P") 3))
  (test empty (mocks (env.get "P" "/notes") (file.read "/notes" "")) (eq? (wc_from_env "P") 0)))""")

add("cap", "http_uppercase_then_count_a",
    "Fetch URL, uppercase body, count occurrences of 'A'.",
    """(module HttpUpA
  (extern defun http_get (u) @capability "http.fetch")
  (defun count_A (url)
    (def b (str_upper (http_get url)))
    (def n (str_length b))
    (def c 0)
    (def i 0)
    (while (< i n)
      (do
        (if (str_eq (str_substring b i 1) "A") (set c (+ c 1)) 0)
        (set i (+ i 1))))
    c)
  (test mix (mocks (http.fetch "http://x" "Apple ant Aardvark")) (eq? (count_A "http://x") 5))
  (test empty (mocks (http.fetch "http://x" "")) (eq? (count_A "http://x") 0))
  (test none (mocks (http.fetch "http://x" "xyz")) (eq? (count_A "http://x") 0)))""")

add("cap", "env_two_files_concat_length",
    "Read two file paths from env, return combined content length.",
    """(module TwoFilesLen
  (extern defun env_read (k) @capability "env.get")
  (extern defun file_read (p) @capability "file.read")
  (defun total_len (k1 k2)
    (+ (str_length (file_read (env_read k1))) (str_length (file_read (env_read k2)))))
  (test t (mocks (env.get "A" "/a") (env.get "B" "/b") (file.read "/a" "hi") (file.read "/b" "world!")) (eq? (total_len "A" "B") 8))
  (test empty (mocks (env.get "A" "/a") (env.get "B" "/b") (file.read "/a" "") (file.read "/b" "")) (eq? (total_len "A" "B") 0)))""")

add("cap", "http_filter_then_count_lines",
    "Fetch URL and count lines (split by '\\n').",
    """(module HttpLines
  (extern defun http_get (u) @capability "http.fetch")
  (defun lines (u)
    (def b (http_get u))
    (if (str_eq b "") (return 0))
    (arr.length (str_split b "\\n")))
  (test one (mocks (http.fetch "http://x" "single")) (eq? (lines "http://x") 1))
  (test multi (mocks (http.fetch "http://x" "a\\nb\\nc")) (eq? (lines "http://x") 3))
  (test empty (mocks (http.fetch "http://x" "")) (eq? (lines "http://x") 0)))""")

add("cap", "env_required_or_error",
    "Return env value, or 'ERROR' if blank (a strict variant).",
    """(module EnvRequired
  (extern defun env_read (k) @capability "env.get")
  (defun strict (k)
    (def v (env_read k))
    (if (str_eq v "") (return "ERROR"))
    v)
  (test miss (mocks (env.get "X" "")) (eq? (strict "X") "ERROR"))
  (test set (mocks (env.get "X" "value")) (eq? (strict "X") "value"))
  (test blank_to_err (mocks (env.get "X" "")) (eq? (strict "X") "ERROR")))""")

add("cap", "file_avg_word_length",
    "Read file content; compute integer average word length (sum of lengths / count); 0 if empty.",
    """(module AvgWordLen
  (extern defun file_read (p) @capability "file.read")
  (defun avg_wl (p)
    (def c (file_read p))
    (if (str_eq c "") (return 0))
    (def parts : (Array Str) (str_split c " "))
    (def n (arr.length parts))
    (def total 0)
    (def i 0)
    (while (< i n)
      (do
        (set total (+ total (str_length (arr.get parts i))))
        (set i (+ i 1))))
    (math.floor (/ total n)))
  (test t (mocks (file.read "/x" "the quick fox")) (eq? (avg_wl "/x") 3))
  (test single (mocks (file.read "/x" "hello")) (eq? (avg_wl "/x") 5))
  (test empty (mocks (file.read "/x" "")) (eq? (avg_wl "/x") 0)))""")

add("cap", "http_fetch_then_first_word",
    "Fetch URL, return first space-separated token, or '' if empty.",
    """(module HttpFirstWord
  (extern defun http_get (u) @capability "http.fetch")
  (defun first_word (u)
    (def b (http_get u))
    (if (str_eq b "") (return ""))
    (arr.get (str_split b " ") 0))
  (test single (mocks (http.fetch "http://x" "hello")) (eq? (first_word "http://x") "hello"))
  (test multi (mocks (http.fetch "http://x" "the cat sat")) (eq? (first_word "http://x") "the"))
  (test empty (mocks (http.fetch "http://x" "")) (eq? (first_word "http://x") "")))""")

add("cap", "env_join_three_with_dash",
    "Read three env vars and join with dashes.",
    """(module EnvJoin3
  (extern defun env_read (k) @capability "env.get")
  (defun join3 (a b c)
    (str_concat (str_concat (str_concat (str_concat (env_read a) "-") (env_read b)) "-") (env_read c)))
  (test all (mocks (env.get "X" "alpha") (env.get "Y" "beta") (env.get "Z" "gamma")) (eq? (join3 "X" "Y" "Z") "alpha-beta-gamma"))
  (test empty_mid (mocks (env.get "X" "x") (env.get "Y" "") (env.get "Z" "z")) (eq? (join3 "X" "Y" "Z") "x--z")))""")

add("cap", "file_count_specific_char",
    "Count occurrences of a single character in a file.",
    """(module FileCountChar
  (extern defun file_read (p) @capability "file.read")
  (defun cc (path c)
    (def s (file_read path))
    (def n (str_length s))
    (def k 0)
    (def i 0)
    (while (< i n)
      (do
        (if (str_eq (str_substring s i 1) c) (set k (+ k 1)) 0)
        (set i (+ i 1))))
    k)
  (test mid (mocks (file.read "/x" "banana")) (eq? (cc "/x" "a") 3))
  (test none (mocks (file.read "/x" "hello")) (eq? (cc "/x" "z") 0))
  (test empty (mocks (file.read "/x" "")) (eq? (cc "/x" "x") 0)))""")

# ============== HOF + recursion ==============

add("pure", "rec_sum_array",
    "Sum array recursively.",
    """(module RecSum
  (defun rsum (a i n)
    (if (= i n) (return 0))
    (+ (arr.get a i) (rsum a (+ i 1) n)))
  (test t (do
    (def a : (Array Num) (arr.new 4))
    (arr.set a 0 1) (arr.set a 1 2) (arr.set a 2 3) (arr.set a 3 4)
    (assert-eq (rsum a 0 4) 10))))""")

add("pure", "rec_max_array",
    "Find max recursively.",
    """(module RecMax
  (defun rmax (a i n best)
    (if (= i n) (return best))
    (def v (arr.get a i))
    (if (> v best) (rmax a (+ i 1) n v) (rmax a (+ i 1) n best)))
  (test t (do
    (def a : (Array Num) (arr.new 5))
    (arr.set a 0 3) (arr.set a 1 8) (arr.set a 2 1) (arr.set a 3 6) (arr.set a 4 9)
    (assert-eq (rmax a 1 5 (arr.get a 0)) 9))))""")

add("pure", "rec_count_zeros",
    "Count zeros in array recursively.",
    """(module RecZeros
  (defun rcz (a i n)
    (if (= i n) (return 0))
    (def rest (rcz a (+ i 1) n))
    (if (= (arr.get a i) 0) (+ rest 1) rest))
  (test t (do
    (def a : (Array Num) (arr.new 6))
    (arr.set a 0 0) (arr.set a 1 1) (arr.set a 2 0) (arr.set a 3 2) (arr.set a 4 0) (arr.set a 5 3)
    (assert-eq (rcz a 0 6) 3))))""")

add("pure", "hof_signum_then_sum",
    "Map x -> sign(x) (-1/0/1), sum the results.",
    """(module HofSign
  (defun sgn (x) (if (> x 0) 1 (if (< x 0) -1 0)))
  (defun add2 (a b) (+ a b))
  (test t (do
    (def a : (Array Num) (arr.new 5))
    (arr.set a 0 -3) (arr.set a 1 0) (arr.set a 2 5) (arr.set a 3 -1) (arr.set a 4 2)
    (def s : (Array Num) (arr.map a sgn))
    (assert-eq (arr.reduce s add2 0) 0))))""")

add("pure", "hof_filter_in_modular_class",
    "Filter values where v mod 4 == 1; sum result.",
    """(module HofModFilter
  (defun mod4_1 (x) (if (= (math.mod x 4) 1) 1 0))
  (defun add2 (a b) (+ a b))
  (test t (do
    (def a : (Array Num) (arr.new 6))
    (arr.set a 0 1) (arr.set a 1 5) (arr.set a 2 9) (arr.set a 3 4) (arr.set a 4 13) (arr.set a 5 7)
    (def f : (Array Num) (arr.filter a mod4_1))
    (assert-eq (arr.reduce f add2 0) 28))))""")

add("pure", "hof_count_via_predicate",
    "Reusable counter: count items where x > threshold.",
    """(module HofCountGT
  (defun gt10 (x) (if (> x 10) 1 0))
  (test t (do
    (def a : (Array Num) (arr.new 6))
    (arr.set a 0 5) (arr.set a 1 10) (arr.set a 2 11) (arr.set a 3 20) (arr.set a 4 9) (arr.set a 5 15)
    (def f : (Array Num) (arr.filter a gt10))
    (assert-eq (arr.length f) 3))))""")

add("pure", "rec_string_count_char",
    "Count occurrences of c in s recursively (slicing via substring).",
    """(module RecStrCount
  (defun rsc (s c i n)
    (if (= i n) (return 0))
    (def rest (rsc s c (+ i 1) n))
    (if (str_eq (str_substring s i 1) c) (+ rest 1) rest))
  (defun count_c (s c) (rsc s c 0 (str_length s)))
  (test mid (eq? (count_c "banana" "a") 3))
  (test none (eq? (count_c "abc" "z") 0))
  (test empty (eq? (count_c "" "a") 0)))""")

add("pure", "rec_reverse_str",
    "Reverse a string recursively (linear allocs).",
    """(module RecRev
  (defun rrev (s i n)
    (if (= i n) (return ""))
    (str_concat (rrev s (+ i 1) n) (str_substring s i 1)))
  (defun rev (s) (rrev s 0 (str_length s)))
  (test t (eq? (rev "hello") "olleh"))
  (test single (eq? (rev "x") "x"))
  (test empty (eq? (rev "") "")))""")

# ============== multi: 3+ helper decomposition ==============

add("multi", "decompose_min_then_max_then_avg",
    "Three small helpers then a combiner returning (min+max+avg) bundled into a string.",
    """(module Triple
  (defun fmin (a n)
    (def m (arr.get a 0))
    (def i 1)
    (while (< i n)
      (do
        (if (< (arr.get a i) m) (set m (arr.get a i)) 0)
        (set i (+ i 1))))
    m)
  (defun fmax (a n)
    (def m (arr.get a 0))
    (def i 1)
    (while (< i n)
      (do
        (if (> (arr.get a i) m) (set m (arr.get a i)) 0)
        (set i (+ i 1))))
    m)
  (defun favg (a n)
    (def s 0.0)
    (def i 0)
    (while (< i n) (do (set s (+ s (arr.get a i))) (set i (+ i 1))))
    (/ s n))
  (defun summary (a n)
    (str_concat (str_concat (str_concat (str_concat (str_concat
      "min=" (str.from_num (fmin a n)))
      ",max=") (str.from_num (fmax a n)))
      ",avg=") (str.from_num (favg a n))))
  (test t (do
    (def a : (Array Num) (arr.new 4))
    (arr.set a 0 2) (arr.set a 1 8) (arr.set a 2 4) (arr.set a 3 6)
    (assert-eq (summary a 4) "min=2,max=8,avg=5"))))""")

add("multi", "decompose_apply_then_count",
    "First helper applies a transform, second filters by predicate, third counts.",
    """(module ApplyCount
  (defun add10 (x) (+ x 10))
  (defun gt15 (x) (if (> x 15) 1 0))
  (defun count_after (a)
    (def m : (Array Num) (arr.map a add10))
    (def f : (Array Num) (arr.filter m gt15))
    (arr.length f))
  (test t (do
    (def a : (Array Num) (arr.new 6))
    (arr.set a 0 1) (arr.set a 1 5) (arr.set a 2 7) (arr.set a 3 9) (arr.set a 4 2) (arr.set a 5 4)
    (assert-eq (count_after a) 2))))""")

add("multi", "decompose_validate_then_clamp_then_label",
    "Validate range, clamp, then label as 'low'/'mid'/'high'.",
    """(module VCL
  (defun valid (x) (if (and (>= x 0) (<= x 100)) 1 0))
  (defun clamp01 (x) (if (< x 0) 0 (if (> x 100) 100 x)))
  (defun lbl (x)
    (if (< x 33) "low" (if (< x 67) "mid" "high")))
  (defun run (x) (lbl (clamp01 x)))
  (test low (eq? (run -10) "low"))
  (test mid (eq? (run 50) "mid"))
  (test high (eq? (run 200) "high")))""")

add("multi", "decompose_pipeline_text_clean",
    "Trim leading/trailing space simulated via length checks; uppercase; concat with prefix.",
    """(module TextClean
  (defun trim_left (s)
    (def n (str.length s))
    (def i 0)
    (def keep_going 1)
    (while (= keep_going 1)
      (do
        (if (>= i n) (set keep_going 0)
          (if (str_eq (str_substring s i 1) " ") (set i (+ i 1)) (set keep_going 0)))))
    (str.substring s i (- n i)))
  (defun upper_clean (s)
    (str.upper (trim_left s)))
  (defun decorate (s)
    (str_concat ">> " (upper_clean s)))
  (test plain (eq? (decorate "hello") ">> HELLO"))
  (test indent (eq? (decorate "   hi") ">> HI"))
  (test empty (eq? (decorate "") ">> ")))""")

add("multi", "decompose_three_helpers_score",
    "Three score adjusters (weight, bonus, penalty) then total.",
    """(module ScoreAdjust
  (defun weighted (s w) (* s w))
  (defun bonus (s b) (+ s b))
  (defun penalty (s p) (- s p))
  (defun final_score (raw w b p)
    (penalty (bonus (weighted raw w) b) p))
  (test base (near? (final_score 10.0 1.0 0.0 0.0) 10.0 0.0001))
  (test scaled (near? (final_score 10.0 2.0 0.0 0.0) 20.0 0.0001))
  (test adj (near? (final_score 10.0 2.0 5.0 3.0) 22.0 0.0001)))""")

add("multi", "decompose_temperature_three_steps",
    "C->F, then >threshold flag, then string label.",
    """(module TempLabel
  (defun c_to_f (c) (+ (* c 1.8) 32.0))
  (defun is_hot (f) (if (> f 80.0) 1 0))
  (defun label (f) (if (= (is_hot f) 1) "HOT" "OK"))
  (defun classify_c (c) (label (c_to_f c)))
  (test cold (eq? (classify_c 10.0) "OK"))
  (test warm (eq? (classify_c 25.0) "OK"))
  (test hot (eq? (classify_c 35.0) "HOT")))""")

# ============== contracts (require/ensure) ==============

add("pure", "contract_safe_div",
    "Safe division with require b != 0; returns a/b.",
    """(module SafeDiv
  (defun safediv (a b)
    (require (not (= b 0)))
    (/ a b))
  (test ok (eq? (safediv 10 2) 5))
  (test pos (eq? (safediv 9 3) 3)))""")

add("pure", "contract_sqrt_nonneg",
    "Require x >= 0 before sqrt.",
    """(module ReqSqrt
  (defun sqrt_safe (x)
    (require (>= x 0.0))
    (math.sqrt x))
  (test zero (near? (sqrt_safe 0.0) 0.0 0.0001))
  (test pos (near? (sqrt_safe 9.0) 3.0 0.0001))
  (test big (near? (sqrt_safe 100.0) 10.0 0.0001)))""")

add("pure", "contract_array_nonempty",
    "First-element accessor with require n > 0.",
    """(module FirstReq
  (defun first (a n)
    (require (> n 0))
    (arr.get a 0))
  (test t (do
    (def a : (Array Num) (arr.new 3))
    (arr.set a 0 99) (arr.set a 1 1) (arr.set a 2 2)
    (assert-eq (first a 3) 99))))""")

# ============== more pure: number/algorithm ==============

add("pure", "smallest_divisor",
    "Return smallest divisor > 1 (so for primes it's n itself).",
    """(module SmallestDiv
  (defun sd (n)
    (def d 2)
    (def found 0)
    (while (and (= found 0) (<= (* d d) n))
      (do
        (if (= (math.mod n d) 0) (set found 1) (set d (+ d 1)))))
    (if (= found 0) n d))
  (test prime (eq? (sd 13) 13))
  (test even (eq? (sd 12) 2))
  (test n9 (eq? (sd 9) 3))
  (test n2 (eq? (sd 2) 2)))""")

add("pure", "largest_prime_factor",
    "Largest prime factor of n (iterative trial division).",
    """(module LargestPF
  (defun lpf (n)
    (def x n)
    (def d 2)
    (def best 1)
    (while (> x 1)
      (do
        (if (= (math.mod x d) 0)
          (do (set best d) (set x (/ x d)))
          (set d (+ d 1)))))
    best)
  (test n12 (eq? (lpf 12) 3))
  (test n13 (eq? (lpf 13) 13))
  (test n100 (eq? (lpf 100) 5)))""")

add("pure", "is_armstrong_general",
    "Generalized Armstrong check: sum of digits^k == n where k is number of digits.",
    """(module ArmGen
  (defun digit_count (n)
    (def x n)
    (def k 0)
    (if (= n 0) (return 1))
    (while (> x 0)
      (do
        (set k (+ k 1))
        (set x (/ (- x (math.mod x 10)) 10))))
    k)
  (defun pow_int (b e)
    (def r 1)
    (def i 0)
    (while (< i e) (do (set r (* r b)) (set i (+ i 1))))
    r)
  (defun digit_pow_sum (n k)
    (def x n)
    (def s 0)
    (while (> x 0)
      (do
        (def d (math.mod x 10))
        (set s (+ s (pow_int d k)))
        (set x (/ (- x d) 10))))
    s)
  (defun is_arm (n)
    (def k (digit_count n))
    (if (= n (digit_pow_sum n k)) 1 0))
  (test a153 (eq? (is_arm 153) 1))
  (test a370 (eq? (is_arm 370) 1))
  (test a9474 (eq? (is_arm 9474) 1))
  (test a100 (eq? (is_arm 100) 0)))""")

add("pure", "fibonacci_sum_to_n",
    "Sum of Fibonacci numbers up to F(n) (inclusive).",
    """(module FibSum
  (defun fsum (n)
    (def a 0) (def b 1) (def s 0) (def i 0)
    (while (<= i n)
      (do
        (set s (+ s a))
        (def t (+ a b))
        (set a b)
        (set b t)
        (set i (+ i 1))))
    s)
  (test n0 (eq? (fsum 0) 0))
  (test n5 (eq? (fsum 5) 12))
  (test n10 (eq? (fsum 10) 143)))""")

add("pure", "count_divisors",
    "Count divisors of n (1 <= d <= n).",
    """(module CountDiv
  (defun cdiv (n)
    (def c 0)
    (def i 1)
    (while (<= i n)
      (do
        (if (= (math.mod n i) 0) (set c (+ c 1)) 0)
        (set i (+ i 1))))
    c)
  (test n1 (eq? (cdiv 1) 1))
  (test n6 (eq? (cdiv 6) 4))
  (test n12 (eq? (cdiv 12) 6))
  (test prime (eq? (cdiv 13) 2)))""")

add("pure", "sum_proper_divisors",
    "Sum of proper divisors (excluding n).",
    """(module SumProperDiv
  (defun spd (n)
    (def s 0)
    (def i 1)
    (while (< i n)
      (do
        (if (= (math.mod n i) 0) (set s (+ s i)) 0)
        (set i (+ i 1))))
    s)
  (test perfect (eq? (spd 6) 6))
  (test prime (eq? (spd 7) 1))
  (test n12 (eq? (spd 12) 16)))""")

add("pure", "is_perfect_number",
    "n is perfect if sum of proper divisors equals n.",
    """(module Perfect
  (defun spd (n)
    (def s 0) (def i 1)
    (while (< i n) (do (if (= (math.mod n i) 0) (set s (+ s i)) 0) (set i (+ i 1))))
    s)
  (defun is_perf (n) (if (= (spd n) n) 1 0))
  (test six (eq? (is_perf 6) 1))
  (test ten (eq? (is_perf 10) 0))
  (test t28 (eq? (is_perf 28) 1)))""")

add("pure", "nth_triangular_number",
    "Triangular number T(n) = n*(n+1)/2.",
    """(module Triangular
  (defun tri (n) (/ (* n (+ n 1)) 2))
  (test n0 (eq? (tri 0) 0))
  (test n5 (eq? (tri 5) 15))
  (test n10 (eq? (tri 10) 55)))""")

add("pure", "nth_pentagonal_number",
    "Pentagonal number P(n) = n*(3n-1)/2.",
    """(module Pent
  (defun pent (n) (/ (* n (- (* 3 n) 1)) 2))
  (test n1 (eq? (pent 1) 1))
  (test n5 (eq? (pent 5) 35))
  (test n10 (eq? (pent 10) 145)))""")

add("pure", "nth_hexagonal_number",
    "Hexagonal number H(n) = n*(2n-1).",
    """(module Hex
  (defun hex (n) (* n (- (* 2 n) 1)))
  (test n1 (eq? (hex 1) 1))
  (test n4 (eq? (hex 4) 28))
  (test n10 (eq? (hex 10) 190)))""")

add("pure", "manhattan_distance_2d",
    "Manhattan distance between two 2D points: |x1-x2|+|y1-y2|.",
    """(module Manhattan
  (defun md (x1 y1 x2 y2)
    (+ (math.abs (- x1 x2)) (math.abs (- y1 y2))))
  (test axis (eq? (md 0 0 3 4) 7))
  (test same (eq? (md 5 5 5 5) 0))
  (test neg (eq? (md -1 -1 2 2) 6)))""")

add("pure", "chebyshev_distance_2d",
    "Chebyshev distance: max(|dx|, |dy|).",
    """(module Chebyshev
  (defun cd (x1 y1 x2 y2)
    (math.max (math.abs (- x1 x2)) (math.abs (- y1 y2))))
  (test axis (eq? (cd 0 0 3 4) 4))
  (test same (eq? (cd 5 5 5 5) 0))
  (test neg (eq? (cd 1 1 -3 0) 4)))""")

add("pure", "celsius_to_kelvin",
    "Celsius to Kelvin: K = C + 273.15.",
    """(module C2K
  (defun c2k (c) (+ c 273.15))
  (test zero (near? (c2k 0.0) 273.15 0.001))
  (test water (near? (c2k 100.0) 373.15 0.001))
  (test below (near? (c2k -273.15) 0.0 0.001)))""")

add("pure", "kelvin_to_fahrenheit",
    "Kelvin to Fahrenheit: F = (K - 273.15) * 9/5 + 32.",
    """(module K2F
  (defun k2f (k) (+ (* (- k 273.15) 1.8) 32.0))
  (test water (near? (k2f 373.15) 212.0 0.001))
  (test ice (near? (k2f 273.15) 32.0 0.001))
  (test absolute (near? (k2f 0.0) -459.67 0.001)))""")

add("pure", "deg_to_rad",
    "Degrees to radians: r = d * pi / 180.",
    """(module D2R
  (defun d2r (d) (/ (* d 3.141592653589793) 180.0))
  (test n0 (near? (d2r 0.0) 0.0 0.0001))
  (test n90 (near? (d2r 90.0) 1.5707963 0.0001))
  (test n180 (near? (d2r 180.0) 3.1415926 0.0001)))""")

add("pure", "rad_to_deg",
    "Radians to degrees.",
    """(module R2D
  (defun r2d (r) (/ (* r 180.0) 3.141592653589793))
  (test n0 (near? (r2d 0.0) 0.0 0.0001))
  (test pi (near? (r2d 3.141592653589793) 180.0 0.0001))
  (test half (near? (r2d 1.5707963267948966) 90.0 0.0001)))""")

add("pure", "kg_to_pounds",
    "Kg to pounds: 1 kg = 2.20462 lb.",
    """(module Kg2Lb
  (defun k2l (k) (* k 2.20462))
  (test one (near? (k2l 1.0) 2.20462 0.0001))
  (test ten (near? (k2l 10.0) 22.0462 0.0001))
  (test zero (near? (k2l 0.0) 0.0 0.0001)))""")

add("pure", "miles_to_meters",
    "Miles to meters: 1 mile = 1609.344 m.",
    """(module Mi2M
  (defun m2m (m) (* m 1609.344))
  (test one (near? (m2m 1.0) 1609.344 0.001))
  (test marathon (near? (m2m 26.2188) 42194.88 1.0)))""")

# ============== more string ops ==============

add("pure", "str_count_letter_case_insensitive",
    "Count occurrences of a letter regardless of case.",
    """(module StrCountCI
  (defun ci_count (s c)
    (def cs (str_upper s))
    (def cc (str_upper c))
    (def n (str_length cs))
    (def k 0)
    (def i 0)
    (while (< i n)
      (do
        (if (str_eq (str_substring cs i 1) cc) (set k (+ k 1)) 0)
        (set i (+ i 1))))
    k)
  (test mid (eq? (ci_count "Banana" "a") 3))
  (test cap (eq? (ci_count "Banana" "B") 1))
  (test miss (eq? (ci_count "hello" "z") 0)))""")

add("pure", "str_starts_with_check",
    "Check whether s starts with prefix p (returns 1/0).",
    """(module StartsWith
  (defun starts (s p)
    (def ns (str_length s))
    (def np (str_length p))
    (if (< ns np) (return 0))
    (if (str_eq (str_substring s 0 np) p) 1 0))
  (test yes (eq? (starts "hello" "hel") 1))
  (test no (eq? (starts "hello" "world") 0))
  (test exact (eq? (starts "hello" "hello") 1))
  (test tooLong (eq? (starts "hi" "hello") 0)))""")

add("pure", "str_ends_with_check",
    "Check whether s ends with suffix x.",
    """(module EndsWith
  (defun ends (s x)
    (def ns (str_length s))
    (def nx (str_length x))
    (if (< ns nx) (return 0))
    (if (str_eq (str_substring s (- ns nx) nx) x) 1 0))
  (test yes (eq? (ends "report.txt" ".txt") 1))
  (test no (eq? (ends "report.txt" ".log") 0))
  (test exact (eq? (ends "abc" "abc") 1))
  (test tooLong (eq? (ends "x" "abc") 0)))""")

add("pure", "str_index_of_char",
    "Index of first occurrence of single char, or -1.",
    """(module IndexOfChar
  (defun idx_of (s c)
    (def n (str_length s))
    (def i 0)
    (def r -1)
    (while (and (< i n) (= r -1))
      (do
        (if (str_eq (str_substring s i 1) c) (set r i) 0)
        (set i (+ i 1))))
    r)
  (test mid (eq? (idx_of "hello" "l") 2))
  (test miss (eq? (idx_of "hello" "z") -1))
  (test first (eq? (idx_of "hello" "h") 0)))""")

add("pure", "str_pad_left",
    "Left-pad string with c to total length n; if already long enough, return unchanged.",
    """(module PadLeft
  (defun padl (s c n)
    (def cur (str_length s))
    (def out s)
    (def need (- n cur))
    (def i 0)
    (while (< i need)
      (do
        (set out (str_concat c out))
        (set i (+ i 1))))
    out)
  (test pad (eq? (padl "5" "0" 3) "005"))
  (test exact (eq? (padl "abc" "x" 3) "abc"))
  (test long (eq? (padl "longer" "x" 3) "longer")))""")

add("pure", "str_pad_right",
    "Right-pad string with c to total length n.",
    """(module PadRight
  (defun padr (s c n)
    (def cur (str_length s))
    (def out s)
    (def need (- n cur))
    (def i 0)
    (while (< i need)
      (do
        (set out (str_concat out c))
        (set i (+ i 1))))
    out)
  (test pad (eq? (padr "ab" "." 5) "ab..."))
  (test exact (eq? (padr "abc" "x" 3) "abc"))
  (test long (eq? (padr "longer" "x" 3) "longer")))""")

# ============== array more ==============

add("pure", "arr_sum_squared_diffs",
    "Sum of squared differences between consecutive elements.",
    """(module SqDiffs
  (defun ssd (a n)
    (def s 0)
    (def i 1)
    (while (< i n)
      (do
        (def d (- (arr.get a i) (arr.get a (- i 1))))
        (set s (+ s (* d d)))
        (set i (+ i 1))))
    s)
  (test t (do
    (def a : (Array Num) (arr.new 4))
    (arr.set a 0 1) (arr.set a 1 3) (arr.set a 2 2) (arr.set a 3 6)
    (assert-eq (ssd a 4) 21))))""")

add("pure", "arr_alternating_sum",
    "Alternating sum: a[0]-a[1]+a[2]-a[3]+...",
    """(module AltSum
  (defun asum (a n)
    (def s 0)
    (def i 0)
    (while (< i n)
      (do
        (if (= (math.mod i 2) 0)
          (set s (+ s (arr.get a i)))
          (set s (- s (arr.get a i))))
        (set i (+ i 1))))
    s)
  (test t (do
    (def a : (Array Num) (arr.new 5))
    (arr.set a 0 10) (arr.set a 1 1) (arr.set a 2 5) (arr.set a 3 2) (arr.set a 4 8)
    (assert-eq (asum a 5) 20))))""")

add("pure", "arr_count_consecutive_dupes",
    "Count adjacent duplicate pairs in array.",
    """(module ConsecDup
  (defun cd (a n)
    (def c 0) (def i 1)
    (while (< i n)
      (do
        (if (= (arr.get a i) (arr.get a (- i 1))) (set c (+ c 1)) 0)
        (set i (+ i 1))))
    c)
  (test t (do
    (def a : (Array Num) (arr.new 6))
    (arr.set a 0 1) (arr.set a 1 1) (arr.set a 2 2) (arr.set a 3 3) (arr.set a 4 3) (arr.set a 5 3)
    (assert-eq (cd a 6) 3))))""")

add("pure", "arr_longest_run_value",
    "Length of the longest run of equal consecutive values.",
    """(module LongestRun
  (defun lr (a n)
    (def best 1)
    (def cur 1)
    (def i 1)
    (while (< i n)
      (do
        (if (= (arr.get a i) (arr.get a (- i 1)))
          (do (set cur (+ cur 1)) (if (> cur best) (set best cur) 0))
          (set cur 1))
        (set i (+ i 1))))
    best)
  (test t (do
    (def a : (Array Num) (arr.new 7))
    (arr.set a 0 1) (arr.set a 1 2) (arr.set a 2 2) (arr.set a 3 2) (arr.set a 4 3) (arr.set a 5 3) (arr.set a 6 4)
    (assert-eq (lr a 7) 3))))""")

add("pure", "arr_max_subarray_two",
    "Max sum of any 2-element subarray.",
    """(module Max2Sub
  (defun ms2 (a n)
    (def best (+ (arr.get a 0) (arr.get a 1)))
    (def i 1)
    (while (< i (- n 1))
      (do
        (def s (+ (arr.get a i) (arr.get a (+ i 1))))
        (if (> s best) (set best s) 0)
        (set i (+ i 1))))
    best)
  (test t (do
    (def a : (Array Num) (arr.new 5))
    (arr.set a 0 -2) (arr.set a 1 1) (arr.set a 2 -3) (arr.set a 3 4) (arr.set a 4 -1)
    (assert-eq (ms2 a 5) 3))))""")

add("pure", "arr_pairwise_product_sum",
    "Sum of pairwise products: a[0]*a[1] + a[1]*a[2] + ...",
    """(module PairProdSum
  (defun pps (a n)
    (def s 0)
    (def i 1)
    (while (< i n)
      (do
        (set s (+ s (* (arr.get a i) (arr.get a (- i 1)))))
        (set i (+ i 1))))
    s)
  (test t (do
    (def a : (Array Num) (arr.new 4))
    (arr.set a 0 1) (arr.set a 1 2) (arr.set a 2 3) (arr.set a 3 4)
    (assert-eq (pps a 4) 20))))""")

# ============== more multi: business-flavored ==============

add("multi", "compute_invoice_with_three_steps",
    "Subtotal -> shipping (flat or 0 if subtotal>=100) -> tax 10% -> final.",
    """(module Invoice
  (defun sub (price qty) (* price qty))
  (defun shipping (s flat) (if (>= s 100.0) 0.0 flat))
  (defun add_tax (x rate) (* x (+ 1.0 rate)))
  (defun invoice (price qty flat tax)
    (add_tax (+ (sub price qty) (shipping (sub price qty) flat)) tax))
  (test small (near? (invoice 5.0 4.0 8.0 0.10) 30.8 0.001))
  (test free_ship (near? (invoice 50.0 3.0 8.0 0.10) 165.0 0.001)))""")

add("multi", "loan_payoff_steps",
    "Compute interest, principal, balance after one payment.",
    """(module LoanStep
  (defun interest_part (b rate) (* b rate))
  (defun principal_part (pmt b rate) (- pmt (interest_part b rate)))
  (defun next_balance (pmt b rate) (- b (principal_part pmt b rate)))
  (test pmt (near? (next_balance 100.0 1000.0 0.01) 910.0 0.001))
  (test interest (near? (interest_part 1000.0 0.01) 10.0 0.0001))
  (test princ (near? (principal_part 100.0 1000.0 0.01) 90.0 0.001)))""")

add("multi", "discount_then_membership_then_tax",
    "Discount on price, member additional 5% off, then 8% tax.",
    """(module DiscMemTax
  (defun base_disc (p d) (* p (- 1.0 d)))
  (defun member_disc (x m) (if (= m 1) (* x 0.95) x))
  (defun add_tax (x rate) (* x (+ 1.0 rate)))
  (defun final (price d m tax)
    (add_tax (member_disc (base_disc price d) m) tax))
  (test guest (near? (final 100.0 0.10 0 0.08) 97.2 0.001))
  (test member (near? (final 100.0 0.10 1 0.08) 92.34 0.001)))""")


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
