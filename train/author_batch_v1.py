#!/usr/bin/env python3
"""Author a stage JSONL by hand. Each pair is human-authored AGC code
in known-working syntax; verify_batch.py is the gate.

Usage:
  python3 author_batch_v1.py > train/stage_claude_v1.jsonl
"""
from __future__ import annotations
import json
import sys


PAIRS: list[tuple[str, str, str, str]] = []  # (category, topic, objective, solution)


def add(category: str, topic: str, objective: str, solution: str) -> None:
    PAIRS.append((category, topic, objective, solution.strip()))


# ---------- pure: number theory & math ----------

add("pure", "harmonic_mean_two",
    "Compute the harmonic mean of two positive numbers a and b.",
    """(module HarmonicMean
  (defun hmean (a b)
    (/ (* 2.0 (* a b)) (+ a b)))
  (test equal (near? (hmean 1.0 1.0) 1.0 0.001))
  (test asym (near? (hmean 4.0 6.0) 4.8 0.001))
  (test wide (near? (hmean 2.0 8.0) 3.2 0.001)))""")

add("pure", "geometric_mean_two",
    "Geometric mean of two positive numbers: sqrt(a*b).",
    """(module GeometricMean
  (defun gmean (a b)
    (math.sqrt (* a b)))
  (test equal (near? (gmean 4.0 4.0) 4.0 0.001))
  (test mix (near? (gmean 2.0 8.0) 4.0 0.001))
  (test one (near? (gmean 1.0 100.0) 10.0 0.001)))""")

add("pure", "modular_exponent",
    "Compute (base^exp) mod m using repeated squaring.",
    """(module ModExp
  (defun modexp (base exp m)
    (def r 1)
    (def b (math.mod base m))
    (def e exp)
    (while (> e 0)
      (do
        (if (= (math.mod e 2) 1) (set r (math.mod (* r b) m)) 0)
        (set b (math.mod (* b b) m))
        (set e (/ (- e (math.mod e 2)) 2))))
    r)
  (test small (eq? (modexp 2 10 1000) 24))
  (test fermat (eq? (modexp 3 5 7) 5))
  (test one (eq? (modexp 7 0 11) 1)))""")

add("pure", "count_set_bits",
    "Count the number of 1-bits in the binary representation of n.",
    """(module PopCount
  (defun popcount (n)
    (def c 0)
    (def x n)
    (while (> x 0)
      (do
        (set c (+ c (math.mod x 2)))
        (set x (/ (- x (math.mod x 2)) 2))))
    c)
  (test zero (eq? (popcount 0) 0))
  (test one (eq? (popcount 1) 1))
  (test seven (eq? (popcount 7) 3))
  (test ten (eq? (popcount 10) 2)))""")

add("pure", "is_perfect_square",
    "Return 1 if n is a perfect square, 0 otherwise.",
    """(module PerfectSquare
  (defun is_psq (n)
    (def r (math.floor (math.sqrt n)))
    (if (= (* r r) n) 1 0))
  (test yes (eq? (is_psq 49) 1))
  (test yes_zero (eq? (is_psq 0) 1))
  (test no (eq? (is_psq 50) 0))
  (test no_two (eq? (is_psq 2) 0)))""")

add("pure", "happy_number_step",
    "Sum of squares of the decimal digits of n (one Happy-number step).",
    """(module HappyStep
  (defun step (n)
    (def s 0)
    (def x n)
    (while (> x 0)
      (do
        (def d (math.mod x 10))
        (set s (+ s (* d d)))
        (set x (/ (- x d) 10))))
    s)
  (test n19 (eq? (step 19) 82))
  (test n7 (eq? (step 7) 49))
  (test n1 (eq? (step 1) 1)))""")

add("pure", "armstrong_three_digit",
    "Return 1 if a 3-digit number equals the sum of cubes of its digits.",
    """(module Armstrong3
  (defun is_arm (n)
    (def a (/ (- n (math.mod n 10)) 10))
    (def d2 (math.mod n 10))
    (def d1 (math.mod a 10))
    (def d0 (/ (- a d1) 10))
    (if (= n (+ (+ (* d0 (* d0 d0)) (* d1 (* d1 d1))) (* d2 (* d2 d2)))) 1 0))
  (test n153 (eq? (is_arm 153) 1))
  (test n370 (eq? (is_arm 370) 1))
  (test n100 (eq? (is_arm 100) 0)))""")

add("pure", "cone_volume",
    "Volume of a cone with radius r and height h: (1/3) * pi * r^2 * h.",
    """(module ConeVolume
  (defun cvol (r h)
    (/ (* (* 3.141592653589793 (* r r)) h) 3.0))
  (test unit (near? (cvol 1.0 3.0) 3.141592653589793 0.0001))
  (test bigger (near? (cvol 2.0 6.0) 25.132741228 0.0001))
  (test zero (near? (cvol 0.0 5.0) 0.0 0.0001)))""")

add("pure", "sphere_volume",
    "Volume of a sphere with radius r: (4/3) * pi * r^3.",
    """(module SphereVolume
  (defun svol (r)
    (* (/ 4.0 3.0) (* 3.141592653589793 (* r (* r r)))))
  (test r1 (near? (svol 1.0) 4.18879020 0.0001))
  (test r2 (near? (svol 2.0) 33.510321638 0.0001))
  (test r0 (near? (svol 0.0) 0.0 0.0001)))""")

add("pure", "distance_3d",
    "Euclidean distance between two 3D points.",
    """(module Distance3D
  (defun d3 (x1 y1 z1 x2 y2 z2)
    (def dx (- x2 x1))
    (def dy (- y2 y1))
    (def dz (- z2 z1))
    (math.sqrt (+ (+ (* dx dx) (* dy dy)) (* dz dz))))
  (test axis (near? (d3 0.0 0.0 0.0 3.0 4.0 0.0) 5.0 0.0001))
  (test diag (near? (d3 1.0 1.0 1.0 4.0 5.0 13.0) 13.0 0.0001))
  (test same (near? (d3 2.0 3.0 4.0 2.0 3.0 4.0) 0.0 0.0001)))""")

add("pure", "linear_interpolate",
    "Linearly interpolate between a and b at parameter t in [0,1].",
    """(module Lerp
  (defun lerp (a b t)
    (+ a (* t (- b a))))
  (test zero (near? (lerp 10.0 20.0 0.0) 10.0 0.0001))
  (test one (near? (lerp 10.0 20.0 1.0) 20.0 0.0001))
  (test half (near? (lerp 10.0 20.0 0.5) 15.0 0.0001))
  (test quarter (near? (lerp 0.0 100.0 0.25) 25.0 0.0001)))""")

add("pure", "midpoint_2d",
    "Compute the 2D midpoint of two points and return x-component.",
    """(module Midpoint
  (defun midx (x1 x2) (/ (+ x1 x2) 2.0))
  (defun midy (y1 y2) (/ (+ y1 y2) 2.0))
  (test mx (near? (midx 0.0 10.0) 5.0 0.0001))
  (test my (near? (midy -4.0 4.0) 0.0 0.0001))
  (test mxneg (near? (midx -3.0 7.0) 2.0 0.0001)))""")

add("pure", "reverse_int_digits",
    "Reverse the digits of a non-negative integer.",
    """(module ReverseInt
  (defun rev (n)
    (def r 0)
    (def x n)
    (while (> x 0)
      (do
        (set r (+ (* r 10) (math.mod x 10)))
        (set x (/ (- x (math.mod x 10)) 10))))
    r)
  (test small (eq? (rev 123) 321))
  (test trail0 (eq? (rev 1200) 21))
  (test single (eq? (rev 7) 7))
  (test zero (eq? (rev 0) 0)))""")

add("pure", "ratio_simplify_num",
    "Simplify a/b by dividing both by gcd and return the numerator.",
    """(module RatioNum
  (defun gcd (a b)
    (if (= b 0) a (gcd b (math.mod a b))))
  (defun num_simplified (a b)
    (/ a (gcd a b)))
  (test half (eq? (num_simplified 6 8) 3))
  (test third (eq? (num_simplified 9 27) 1))
  (test coprime (eq? (num_simplified 5 7) 5)))""")

add("pure", "is_power_of_three",
    "Return 1 if n is a power of 3 (1, 3, 9, 27, ...), 0 otherwise.",
    """(module PowerOfThree
  (defun is_p3 (n)
    (def x n)
    (if (<= x 0) 0
      (do
        (while (= (math.mod x 3) 0) (set x (/ x 3)))
        (if (= x 1) 1 0))))
  (test one (eq? (is_p3 1) 1))
  (test nine (eq? (is_p3 9) 1))
  (test twentyseven (eq? (is_p3 27) 1))
  (test six (eq? (is_p3 6) 0))
  (test zero (eq? (is_p3 0) 0)))""")

# ---------- pure: array transforms (gaps) ----------

add("pure", "prefix_sum_array_get",
    "Return prefix-sum array; helper returns sum up through index k.",
    """(module PrefixSum
  (defun psum_to (a k)
    (def s 0)
    (def i 0)
    (while (<= i k) (do (set s (+ s (arr.get a i))) (set i (+ i 1))))
    s)
  (test t (do
    (def a : (Array Num) (arr.new 4))
    (arr.set a 0 1) (arr.set a 1 2) (arr.set a 2 3) (arr.set a 3 4)
    (assert-eq (psum_to a 0) 1)
    (assert-eq (psum_to a 2) 6)
    (assert-eq (psum_to a 3) 10))))""")

add("pure", "running_max_at",
    "Return the running maximum of array a at index k (max of a[0..k]).",
    """(module RunningMax
  (defun rmax_at (a k)
    (def m (arr.get a 0))
    (def i 1)
    (while (<= i k)
      (do
        (if (> (arr.get a i) m) (set m (arr.get a i)) 0)
        (set i (+ i 1))))
    m)
  (test t (do
    (def a : (Array Num) (arr.new 5))
    (arr.set a 0 3) (arr.set a 1 1) (arr.set a 2 4) (arr.set a 3 1) (arr.set a 4 5)
    (assert-eq (rmax_at a 0) 3)
    (assert-eq (rmax_at a 2) 4)
    (assert-eq (rmax_at a 4) 5))))""")

add("pure", "rotate_left_one",
    "Rotate array left by one position; check element at index 0.",
    """(module RotateLeft
  (defun rotL (a n)
    (def first (arr.get a 0))
    (def i 0)
    (while (< i (- n 1))
      (do
        (arr.set a i (arr.get a (+ i 1)))
        (set i (+ i 1))))
    (arr.set a (- n 1) first)
    a)
  (test t (do
    (def a : (Array Num) (arr.new 4))
    (arr.set a 0 1) (arr.set a 1 2) (arr.set a 2 3) (arr.set a 3 4)
    (rotL a 4)
    (assert-eq (arr.get a 0) 2)
    (assert-eq (arr.get a 3) 1))))""")

add("pure", "rotate_right_one",
    "Rotate array right by one position.",
    """(module RotateRight
  (defun rotR (a n)
    (def last (arr.get a (- n 1)))
    (def i (- n 1))
    (while (> i 0)
      (do
        (arr.set a i (arr.get a (- i 1)))
        (set i (- i 1))))
    (arr.set a 0 last)
    a)
  (test t (do
    (def a : (Array Num) (arr.new 4))
    (arr.set a 0 1) (arr.set a 1 2) (arr.set a 2 3) (arr.set a 3 4)
    (rotR a 4)
    (assert-eq (arr.get a 0) 4)
    (assert-eq (arr.get a 1) 1))))""")

add("pure", "dedupe_sorted_count",
    "Count distinct elements in a sorted array (consecutive-dup removal).",
    """(module DedupeSorted
  (defun unique_count (a n)
    (if (= n 0) 0
      (do
        (def c 1)
        (def i 1)
        (while (< i n)
          (do
            (if (not (= (arr.get a i) (arr.get a (- i 1)))) (set c (+ c 1)) 0)
            (set i (+ i 1))))
        c)))
  (test t (do
    (def a : (Array Num) (arr.new 6))
    (arr.set a 0 1) (arr.set a 1 1) (arr.set a 2 2) (arr.set a 3 3) (arr.set a 4 3) (arr.set a 5 4)
    (assert-eq (unique_count a 6) 4))))""")

add("pure", "concat_two_arrays_sum",
    "Concatenate two arrays and return the sum of the result.",
    """(module ConcatSum
  (defun csum (a m b n)
    (def s 0)
    (def i 0)
    (while (< i m) (do (set s (+ s (arr.get a i))) (set i (+ i 1))))
    (set i 0)
    (while (< i n) (do (set s (+ s (arr.get b i))) (set i (+ i 1))))
    s)
  (test t (do
    (def a : (Array Num) (arr.new 3))
    (arr.set a 0 1) (arr.set a 1 2) (arr.set a 2 3)
    (def b : (Array Num) (arr.new 2))
    (arr.set b 0 10) (arr.set b 1 20)
    (assert-eq (csum a 3 b 2) 36))))""")

add("pure", "histogram_bucket_count",
    "Count how many values fall in [lo, hi).",
    """(module HistBucket
  (defun count_range (a n lo hi)
    (def c 0)
    (def i 0)
    (while (< i n)
      (do
        (def v (arr.get a i))
        (if (and (>= v lo) (< v hi)) (set c (+ c 1)) 0)
        (set i (+ i 1))))
    c)
  (test t (do
    (def a : (Array Num) (arr.new 6))
    (arr.set a 0 5) (arr.set a 1 12) (arr.set a 2 8) (arr.set a 3 15) (arr.set a 4 3) (arr.set a 5 9)
    (assert-eq (count_range a 6 0 10) 4)
    (assert-eq (count_range a 6 10 20) 2))))""")

add("pure", "find_majority_count",
    "Return the count of the most-occurring value (linear scan, exhaustive).",
    """(module Majority
  (defun maj_count (a n)
    (def best 0)
    (def i 0)
    (while (< i n)
      (do
        (def v (arr.get a i))
        (def c 0)
        (def j 0)
        (while (< j n)
          (do
            (if (= (arr.get a j) v) (set c (+ c 1)) 0)
            (set j (+ j 1))))
        (if (> c best) (set best c) 0)
        (set i (+ i 1))))
    best)
  (test t (do
    (def a : (Array Num) (arr.new 7))
    (arr.set a 0 3) (arr.set a 1 1) (arr.set a 2 3) (arr.set a 3 3) (arr.set a 4 2) (arr.set a 5 3) (arr.set a 6 1)
    (assert-eq (maj_count a 7) 4))))""")

add("pure", "is_palindrome_arr",
    "Check whether an integer array reads the same forwards and backwards.",
    """(module ArrPalindrome
  (defun ispal (a n)
    (def ok 1)
    (def i 0)
    (def j (- n 1))
    (while (< i j)
      (do
        (if (not (= (arr.get a i) (arr.get a j))) (set ok 0) 0)
        (set i (+ i 1))
        (set j (- j 1))))
    ok)
  (test yes (do
    (def a : (Array Num) (arr.new 5))
    (arr.set a 0 1) (arr.set a 1 2) (arr.set a 2 3) (arr.set a 3 2) (arr.set a 4 1)
    (assert-eq (ispal a 5) 1)))
  (test no (do
    (def a : (Array Num) (arr.new 4))
    (arr.set a 0 1) (arr.set a 1 2) (arr.set a 2 3) (arr.set a 3 4)
    (assert-eq (ispal a 4) 0))))""")

add("pure", "intersect_two_count",
    "Count how many values from b also appear in a (with repeats).",
    """(module IntersectCount
  (defun ic (a m b n)
    (def c 0)
    (def i 0)
    (while (< i n)
      (do
        (def v (arr.get b i))
        (def j 0)
        (def found 0)
        (while (< j m)
          (do
            (if (= (arr.get a j) v) (set found 1) 0)
            (set j (+ j 1))))
        (if (= found 1) (set c (+ c 1)) 0)
        (set i (+ i 1))))
    c)
  (test t (do
    (def a : (Array Num) (arr.new 4))
    (arr.set a 0 1) (arr.set a 1 2) (arr.set a 2 3) (arr.set a 3 4)
    (def b : (Array Num) (arr.new 3))
    (arr.set b 0 2) (arr.set b 1 5) (arr.set b 2 4)
    (assert-eq (ic a 4 b 3) 2))))""")

add("pure", "second_max_in_array",
    "Return the second-largest value in an array of distinct integers.",
    """(module SecondMax
  (defun smax (a n)
    (def m1 (arr.get a 0))
    (def m2 -1000000000)
    (def i 1)
    (while (< i n)
      (do
        (def v (arr.get a i))
        (if (> v m1) (do (set m2 m1) (set m1 v))
                     (if (> v m2) (set m2 v) 0))
        (set i (+ i 1))))
    m2)
  (test t (do
    (def a : (Array Num) (arr.new 5))
    (arr.set a 0 3) (arr.set a 1 8) (arr.set a 2 1) (arr.set a 3 9) (arr.set a 4 4)
    (assert-eq (smax a 5) 8))))""")

add("pure", "two_sum_exists",
    "Return 1 if any pair of distinct indices sums to target, 0 otherwise.",
    """(module TwoSum
  (defun has2 (a n target)
    (def found 0)
    (def i 0)
    (while (< i n)
      (do
        (def j (+ i 1))
        (while (< j n)
          (do
            (if (= (+ (arr.get a i) (arr.get a j)) target) (set found 1) 0)
            (set j (+ j 1))))
        (set i (+ i 1))))
    found)
  (test yes (do
    (def a : (Array Num) (arr.new 4))
    (arr.set a 0 2) (arr.set a 1 7) (arr.set a 2 11) (arr.set a 3 15)
    (assert-eq (has2 a 4 9) 1)))
  (test no (do
    (def a : (Array Num) (arr.new 3))
    (arr.set a 0 1) (arr.set a 1 2) (arr.set a 2 3)
    (assert-eq (has2 a 3 100) 0))))""")

# ---------- pure: string algorithms (gaps) ----------

add("pure", "str_reverse_iter",
    "Reverse a string by iterating with str.substring.",
    """(module StrReverse
  (defun rev (s)
    (def n (str.length s))
    (def out "")
    (def i 0)
    (while (< i n)
      (do
        (set out (str.concat (str.substring s i 1) out))
        (set i (+ i 1))))
    out)
  (test hello (eq? (rev "hello") "olleh"))
  (test empty (eq? (rev "") ""))
  (test single (eq? (rev "x") "x"))
  (test pal (eq? (rev "abba") "abba")))""")

add("pure", "is_palindrome_str_iter",
    "Check whether a string is a palindrome (case-sensitive).",
    """(module StrPal
  (defun ispal (s)
    (def n (str.length s))
    (def ok 1)
    (def i 0)
    (def j (- n 1))
    (while (< i j)
      (do
        (if (not (= (str.substring s i 1) (str.substring s j 1))) (set ok 0) 0)
        (set i (+ i 1))
        (set j (- j 1))))
    ok)
  (test yes (eq? (ispal "racecar") 1))
  (test no (eq? (ispal "hello") 0))
  (test empty (eq? (ispal "") 1))
  (test single (eq? (ispal "x") 1)))""")

add("pure", "swap_case_simple",
    "Swap upper- and lower-case using str.upper/str.lower per char.",
    """(module SwapCase
  (defun swap (s)
    (def n (str.length s))
    (def out "")
    (def i 0)
    (while (< i n)
      (do
        (def c (str.substring s i 1))
        (def u (str.upper c))
        (set out (str.concat out (if (= c u) (str.lower c) u)))
        (set i (+ i 1))))
    out)
  (test mixed (= (swap "Hello") "hELLO"))
  (test all_lower (= (swap "abc") "ABC"))
  (test all_upper (= (swap "XYZ") "xyz")))""")

add("pure", "compress_repeats_count",
    "Run-length compression: count of distinct adjacent groups.",
    """(module RLEGroups
  (defun groups (s)
    (def n (str.length s))
    (if (= n 0) 0
      (do
        (def c 1)
        (def i 1)
        (while (< i n)
          (do
            (if (not (= (str.substring s i 1) (str.substring s (- i 1) 1))) (set c (+ c 1)) 0)
            (set i (+ i 1))))
        c)))
  (test aabb (eq? (groups "aabb") 2))
  (test mix (eq? (groups "aaabbc") 3))
  (test all_same (eq? (groups "aaaa") 1))
  (test empty (eq? (groups "") 0)))""")

add("pure", "remove_char_count",
    "Count length of string s after removing all occurrences of char c.",
    """(module RemoveChar
  (defun rmlen (s c)
    (def n (str.length s))
    (def kept 0)
    (def i 0)
    (while (< i n)
      (do
        (if (not (= (str.substring s i 1) c)) (set kept (+ kept 1)) 0)
        (set i (+ i 1))))
    kept)
  (test mid (eq? (rmlen "banana" "a") 3))
  (test none (eq? (rmlen "banana" "x") 6))
  (test all (eq? (rmlen "aaaa" "a") 0)))""")

add("pure", "first_char",
    "Return the first character of s, or empty if length 0.",
    """(module FirstChar
  (defun fc (s)
    (if (= (str.length s) 0) "" (str.substring s 0 1)))
  (test ok (eq? (fc "hello") "h"))
  (test single (eq? (fc "x") "x"))
  (test empty (eq? (fc "") "")))""")

add("pure", "last_char",
    "Return the last character of s, or empty if length 0.",
    """(module LastChar
  (defun lc (s)
    (def n (str.length s))
    (if (= n 0) "" (str.substring s (- n 1) 1)))
  (test ok (eq? (lc "hello") "o"))
  (test single (eq? (lc "x") "x"))
  (test empty (eq? (lc "") "")))""")

add("pure", "truncate_with_ellipsis",
    "Truncate s to max-len chars; if longer, append '...'.",
    """(module Truncate
  (defun trunc (s n)
    (if (<= (str.length s) n) s (str.concat (str.substring s 0 n) "...")))
  (test short (eq? (trunc "hi" 5) "hi"))
  (test exact (eq? (trunc "hello" 5) "hello"))
  (test long (eq? (trunc "hello world" 5) "hello...")))""")

add("pure", "count_uppercase_chars",
    "Count number of uppercase characters in s.",
    """(module CountUpper
  (defun cu (s)
    (def n (str.length s))
    (def c 0)
    (def i 0)
    (while (< i n)
      (do
        (def ch (str.substring s i 1))
        (if (and (= ch (str.upper ch)) (not (= ch (str.lower ch)))) (set c (+ c 1)) 0)
        (set i (+ i 1))))
    c)
  (test mix (eq? (cu "HelloWorld") 2))
  (test none (eq? (cu "hello") 0))
  (test all (eq? (cu "ABC") 3)))""")

add("pure", "repeat_string_n",
    "Repeat string s n times.",
    """(module RepeatStr
  (defun rep (s n)
    (def out "")
    (def i 0)
    (while (< i n)
      (do
        (set out (str.concat out s))
        (set i (+ i 1))))
    out)
  (test three (eq? (rep "ab" 3) "ababab"))
  (test zero (eq? (rep "abc" 0) ""))
  (test one (eq? (rep "x" 1) "x")))""")

# ---------- pure: higher-order functions ----------

add("pure", "hof_map_double_sum",
    "Map x->2x over an array, then sum the results.",
    """(module HofMapDouble
  (defun dbl (x) (* x 2))
  (defun add2 (a b) (+ a b))
  (test t (do
    (def a : (Array Num) (arr.new 4))
    (arr.set a 0 1) (arr.set a 1 2) (arr.set a 2 3) (arr.set a 3 4)
    (def b : (Array Num) (arr.map a dbl))
    (assert-eq (arr.reduce b add2 0) 20))))""")

add("pure", "hof_filter_positive_count",
    "Filter for positive ints and count them.",
    """(module HofFilterPos
  (defun pos (x) (if (> x 0) 1 0))
  (test t (do
    (def a : (Array Num) (arr.new 5))
    (arr.set a 0 -1) (arr.set a 1 2) (arr.set a 2 -3) (arr.set a 3 4) (arr.set a 4 0)
    (def b : (Array Num) (arr.filter a pos))
    (assert-eq (arr.length b) 2))))""")

add("pure", "hof_reduce_product",
    "Reduce array via multiplication.",
    """(module HofReduceProd
  (defun mul (a b) (* a b))
  (test t (do
    (def a : (Array Num) (arr.new 4))
    (arr.set a 0 1) (arr.set a 1 2) (arr.set a 2 3) (arr.set a 3 4)
    (assert-eq (arr.reduce a mul 1) 24))))""")

add("pure", "hof_square_then_filter_then_sum",
    "Square each element, keep those above 5, sum result.",
    """(module HofPipeline
  (defun sq (x) (* x x))
  (defun gt5 (x) (if (> x 5) 1 0))
  (defun add2 (a b) (+ a b))
  (test t (do
    (def a : (Array Num) (arr.new 5))
    (arr.set a 0 1) (arr.set a 1 2) (arr.set a 2 3) (arr.set a 3 4) (arr.set a 4 5)
    (def squared : (Array Num) (arr.map a sq))
    (def big : (Array Num) (arr.filter squared gt5))
    (assert-eq (arr.reduce big add2 0) 50))))""")

add("pure", "hof_count_even_via_filter",
    "Count evens using arr.filter + arr.length.",
    """(module HofCountEven
  (defun even1 (x) (if (= (math.mod x 2) 0) 1 0))
  (test t (do
    (def a : (Array Num) (arr.new 6))
    (arr.set a 0 1) (arr.set a 1 2) (arr.set a 2 3) (arr.set a 3 4) (arr.set a 4 5) (arr.set a 5 6)
    (def evens : (Array Num) (arr.filter a even1))
    (assert-eq (arr.length evens) 3))))""")

add("pure", "hof_negate_then_max",
    "Negate every value, then take the max via reduce.",
    """(module HofNegMax
  (defun neg (x) (- 0 x))
  (defun mx (a b) (if (> a b) a b))
  (test t (do
    (def a : (Array Num) (arr.new 4))
    (arr.set a 0 1) (arr.set a 1 5) (arr.set a 2 3) (arr.set a 3 2)
    (def neg_a : (Array Num) (arr.map a neg))
    (assert-eq (arr.reduce neg_a mx -1000) -1))))""")

add("pure", "hof_filter_in_range_then_count",
    "Filter values in [10,20] then count.",
    """(module HofRange
  (defun in_range (x) (if (and (>= x 10) (<= x 20)) 1 0))
  (test t (do
    (def a : (Array Num) (arr.new 6))
    (arr.set a 0 5) (arr.set a 1 10) (arr.set a 2 15) (arr.set a 3 20) (arr.set a 4 25) (arr.set a 5 12)
    (def in : (Array Num) (arr.filter a in_range))
    (assert-eq (arr.length in) 4))))""")

# ---------- pure: map operations ----------

add("pure", "map_word_freq_size",
    "Build a frequency map from a space-separated string; return size.",
    """(module MapFreqSize
  (defun build_freq (s)
    (def parts : (Array Str) (str.split s " "))
    (def n (arr.length parts))
    (def m : (Map Str Num) (map.new))
    (def i 0)
    (while (< i n)
      (do
        (def k : Str (arr.get parts i))
        (if (map.has m k) (map.set m k (+ (map.get m k) 1)) (map.set m k 1))
        (set i (+ i 1))))
    m)
  (test t (do
    (def m : (Map Str Num) (build_freq "x y x z y"))
    (assert-eq (map.size m) 3)
    (assert-eq (map.get m "x") 2)
    (assert-eq (map.get m "y") 2)
    (assert-eq (map.get m "z") 1))))""")

add("pure", "map_increment_or_init",
    "Increment a counter under key k, initializing to 1 if absent.",
    """(module MapIncOrInit
  (defun inc (m k)
    (if (map.has m k) (map.set m k (+ (map.get m k) 1)) (map.set m k 1))
    m)
  (test t (do
    (def m : (Map Str Num) (map.new))
    (inc m "a")
    (inc m "a")
    (inc m "b")
    (assert-eq (map.get m "a") 2)
    (assert-eq (map.get m "b") 1)
    (assert-eq (map.size m) 2))))""")

add("pure", "map_total_via_keys_loop",
    "Sum all values in a Map<Str,Num> by iterating keys.",
    """(module MapTotal
  (defun total (m)
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
    (map.set m "a" 10)
    (map.set m "b" 20)
    (map.set m "c" 30)
    (assert-eq (total m) 60))))""")

add("pure", "map_max_value",
    "Find the largest value in a Map<Str,Num>.",
    """(module MapMaxVal
  (defun max_val (m)
    (def keys : (Array Str) (map.keys m))
    (def n (arr.length keys))
    (def best (map.get m (arr.get keys 0)))
    (def i 1)
    (while (< i n)
      (do
        (def v (map.get m (arr.get keys i)))
        (if (> v best) (set best v) 0)
        (set i (+ i 1))))
    best)
  (test t (do
    (def m : (Map Str Num) (map.new))
    (map.set m "a" 3)
    (map.set m "b" 9)
    (map.set m "c" 5)
    (assert-eq (max_val m) 9))))""")

add("pure", "map_remove_then_size",
    "Insert three keys, remove one, check size.",
    """(module MapRemoveSize
  (test t (do
    (def m : (Map Str Num) (map.new))
    (map.set m "a" 1)
    (map.set m "b" 2)
    (map.set m "c" 3)
    (map.remove m "b")
    (assert-eq (map.size m) 2)
    (assert-eq (map.has m "b") 0))))""")

add("pure", "map_group_by_parity_size",
    "Split numbers into 'even'/'odd' buckets in a map; return size 2.",
    """(module MapGroupParity
  (defun group (a n)
    (def m : (Map Str Num) (map.new))
    (def i 0)
    (while (< i n)
      (do
        (def k (if (= (math.mod (arr.get a i) 2) 0) "even" "odd"))
        (if (map.has m k) (map.set m k (+ (map.get m k) 1)) (map.set m k 1))
        (set i (+ i 1))))
    m)
  (test t (do
    (def a : (Array Num) (arr.new 6))
    (arr.set a 0 1) (arr.set a 1 2) (arr.set a 2 3) (arr.set a 3 4) (arr.set a 4 5) (arr.set a 5 6)
    (def m : (Map Str Num) (group a 6))
    (assert-eq (map.size m) 2)
    (assert-eq (map.get m "even") 3)
    (assert-eq (map.get m "odd") 3))))""")

# ---------- multi: small multi-function pipelines ----------

add("multi", "stats_mean_then_var",
    "Compute mean then population variance over a small array.",
    """(module StatsPipe
  (defun mean_n (a n)
    (def s 0.0)
    (def i 0)
    (while (< i n) (do (set s (+ s (arr.get a i))) (set i (+ i 1))))
    (/ s n))
  (defun var_n (a n)
    (def mu (mean_n a n))
    (def s 0.0)
    (def i 0)
    (while (< i n)
      (do
        (def d (- (arr.get a i) mu))
        (set s (+ s (* d d)))
        (set i (+ i 1))))
    (/ s n))
  (test t (do
    (def a : (Array Num) (arr.new 4))
    (arr.set a 0 2.0) (arr.set a 1 4.0) (arr.set a 2 4.0) (arr.set a 3 4.0)
    (assert-near (mean_n a 4) 3.5 0.001)
    (assert-near (var_n a 4) 0.75 0.001))))""")

add("multi", "min_max_range_combo",
    "Three helpers: find_min, find_max, then range = max-min.",
    """(module MinMaxRange
  (defun find_min (a n)
    (def m (arr.get a 0))
    (def i 1)
    (while (< i n)
      (do
        (if (< (arr.get a i) m) (set m (arr.get a i)) 0)
        (set i (+ i 1))))
    m)
  (defun find_max (a n)
    (def m (arr.get a 0))
    (def i 1)
    (while (< i n)
      (do
        (if (> (arr.get a i) m) (set m (arr.get a i)) 0)
        (set i (+ i 1))))
    m)
  (defun arange (a n)
    (- (find_max a n) (find_min a n)))
  (test t (do
    (def a : (Array Num) (arr.new 5))
    (arr.set a 0 3) (arr.set a 1 1) (arr.set a 2 4) (arr.set a 3 1) (arr.set a 4 9)
    (assert-eq (find_min a 5) 1)
    (assert-eq (find_max a 5) 9)
    (assert-eq (arange a 5) 8))))""")

add("multi", "score_then_grade",
    "Average of three scores then map to letter grade.",
    """(module ScoreGrade
  (defun mean3 (a b c) (/ (+ (+ a b) c) 3.0))
  (defun grade (avg)
    (if (>= avg 90.0) "A"
      (if (>= avg 80.0) "B"
        (if (>= avg 70.0) "C"
          (if (>= avg 60.0) "D" "F")))))
  (test a (eq? (grade (mean3 95.0 92.0 88.0)) "A"))
  (test b (eq? (grade (mean3 80.0 80.0 80.0)) "B"))
  (test f (eq? (grade (mean3 50.0 40.0 30.0)) "F")))""")

add("multi", "normalize_then_dot",
    "Compute Euclidean norm, then divide each component (unit-norm check).",
    """(module Normalize
  (defun norm (x y z)
    (math.sqrt (+ (+ (* x x) (* y y)) (* z z))))
  (defun unit_x (x y z) (/ x (norm x y z)))
  (test axis (near? (unit_x 3.0 0.0 0.0) 1.0 0.0001))
  (test diag (near? (unit_x 1.0 1.0 0.0) 0.7071067811 0.0001)))""")

add("multi", "discount_then_tax_then_round",
    "Apply discount, then tax, then round to two decimals (math.floor on *100).",
    """(module DiscTaxRound
  (defun discounted (price d) (* price (- 1.0 d)))
  (defun taxed (p tax) (* p (+ 1.0 tax)))
  (defun round2 (x) (/ (math.floor (+ (* x 100.0) 0.5)) 100.0))
  (defun final_price (price d tax)
    (round2 (taxed (discounted price d) tax)))
  (test ten_off_8tax (near? (final_price 100.0 0.10 0.08) 97.20 0.001))
  (test no_disc (near? (final_price 50.0 0.0 0.0) 50.0 0.001)))""")

add("multi", "validate_then_route",
    "Validate inputs in [1,5] then route to text labels via three helpers.",
    """(module ValidateRoute
  (defun valid (n) (if (and (>= n 1) (<= n 5)) 1 0))
  (defun label (n)
    (if (= n 1) "one"
      (if (= n 2) "two"
        (if (= n 3) "three"
          (if (= n 4) "four"
            (if (= n 5) "five" "out"))))))
  (defun classify (n) (if (= (valid n) 1) (label n) "out"))
  (test ok (eq? (classify 3) "three"))
  (test high (eq? (classify 9) "out"))
  (test low (eq? (classify 0) "out")))""")


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
