#!/usr/bin/env python3
"""Batch v2: capability-track problems + more HOF/multi/pure.

Style rules learned from probing:
  - eq?/near? are assert forms at TEST top-level only.
  - Inside expressions (if, body), use = for strings and numbers, or str_eq.
  - Arrays of strings cannot be made via (arr.new + arr.set); use str.split or map.keys.
  - Use the underscore-style stdlib names too (str_upper, str_length, str_to_num, etc.).
"""
from __future__ import annotations
import json
import sys


PAIRS: list[tuple[str, str, str, str]] = []


def add(category: str, topic: str, objective: str, solution: str) -> None:
    PAIRS.append((category, topic, objective, solution.strip()))


# ============== capability: env ==============

add("cap", "env_default_when_blank",
    "Return env value, or fall back to a default if the value is blank.",
    """(module EnvDefaultBlank
  (extern defun env_read (k) @capability "env.get")
  (defun resolve (k default)
    (def v (env_read k))
    (if (str_eq v "") (return default))
    v)
  (test missing (mocks (env.get "GREETING" "")) (eq? (resolve "GREETING" "hi") "hi"))
  (test set (mocks (env.get "GREETING" "hello")) (eq? (resolve "GREETING" "hi") "hello"))
  (test space (mocks (env.get "GREETING" " ")) (eq? (resolve "GREETING" "hi") " ")))""")

add("cap", "env_int_double",
    "Read env var as int and double it; default to 0 if missing.",
    """(module EnvDouble
  (extern defun env_read (k) @capability "env.get")
  (defun double_env (k)
    (def v (env_read k))
    (if (str_eq v "") (return 0))
    (* (str_to_num v) 2))
  (test missing (mocks (env.get "N" "")) (eq? (double_env "N") 0))
  (test five (mocks (env.get "N" "5")) (eq? (double_env "N") 10))
  (test zero (mocks (env.get "N" "0")) (eq? (double_env "N") 0)))""")

add("cap", "env_concat_two_strings",
    "Read two env vars and concatenate them with a single space.",
    """(module EnvConcat
  (extern defun env_read (k) @capability "env.get")
  (defun cat_two (k1 k2)
    (str_concat (str_concat (env_read k1) " ") (env_read k2)))
  (test both (mocks (env.get "A" "hello") (env.get "B" "world")) (eq? (cat_two "A" "B") "hello world"))
  (test empty_a (mocks (env.get "A" "") (env.get "B" "x")) (eq? (cat_two "A" "B") " x"))
  (test empty_b (mocks (env.get "A" "x") (env.get "B" "")) (eq? (cat_two "A" "B") "x ")))""")

add("cap", "env_length_or_zero",
    "Length of env value, 0 if missing.",
    """(module EnvLen
  (extern defun env_read (k) @capability "env.get")
  (defun env_len (k)
    (str_length (env_read k)))
  (test empty (mocks (env.get "X" "")) (eq? (env_len "X") 0))
  (test five (mocks (env.get "X" "abcde")) (eq? (env_len "X") 5))
  (test ten (mocks (env.get "X" "1234567890")) (eq? (env_len "X") 10)))""")

add("cap", "env_first_char_or_q",
    "Return the first character of env var, or '?' if blank.",
    """(module EnvFirst
  (extern defun env_read (k) @capability "env.get")
  (defun first_or (k)
    (def v (env_read k))
    (if (str_eq v "") (return "?"))
    (str_substring v 0 1))
  (test missing (mocks (env.get "K" "")) (eq? (first_or "K") "?"))
  (test set (mocks (env.get "K" "hello")) (eq? (first_or "K") "h"))
  (test single (mocks (env.get "K" "x")) (eq? (first_or "K") "x")))""")

add("cap", "env_int_in_range",
    "Read env as int and check whether in [10,20]; return 1 or 0.",
    """(module EnvRange
  (extern defun env_read (k) @capability "env.get")
  (defun in_range (k)
    (def v (env_read k))
    (if (str_eq v "") (return 0))
    (def n (str_to_num v))
    (if (and (>= n 10) (<= n 20)) 1 0))
  (test missing (mocks (env.get "N" "")) (eq? (in_range "N") 0))
  (test low (mocks (env.get "N" "5")) (eq? (in_range "N") 0))
  (test mid (mocks (env.get "N" "15")) (eq? (in_range "N") 1))
  (test high (mocks (env.get "N" "21")) (eq? (in_range "N") 0))
  (test edge (mocks (env.get "N" "10")) (eq? (in_range "N") 1)))""")

add("cap", "env_repeat_three",
    "Read env value and repeat it three times concatenated.",
    """(module EnvRepeat
  (extern defun env_read (k) @capability "env.get")
  (defun rep3 (k)
    (def v (env_read k))
    (str_concat (str_concat v v) v))
  (test empty (mocks (env.get "K" "")) (eq? (rep3 "K") ""))
  (test ab (mocks (env.get "K" "ab")) (eq? (rep3 "K") "ababab"))
  (test x (mocks (env.get "K" "x")) (eq? (rep3 "K") "xxx")))""")

add("cap", "env_uppercase_first",
    "Return uppercase of first char of env value, empty if missing.",
    """(module EnvUpFirst
  (extern defun env_read (k) @capability "env.get")
  (defun uf (k)
    (def v (env_read k))
    (if (str_eq v "") (return ""))
    (str_upper (str_substring v 0 1)))
  (test missing (mocks (env.get "X" "")) (eq? (uf "X") ""))
  (test lower (mocks (env.get "X" "hello")) (eq? (uf "X") "H"))
  (test already (mocks (env.get "X" "Quiet")) (eq? (uf "X") "Q")))""")

add("cap", "env_sum_two_ints",
    "Read two env vars as ints and sum; missing = 0.",
    """(module EnvSum2
  (extern defun env_read (k) @capability "env.get")
  (defun int_or0 (k)
    (def v (env_read k))
    (if (str_eq v "") (return 0))
    (str_to_num v))
  (defun sum2 (k1 k2) (+ (int_or0 k1) (int_or0 k2)))
  (test both (mocks (env.get "A" "5") (env.get "B" "7")) (eq? (sum2 "A" "B") 12))
  (test miss_b (mocks (env.get "A" "5") (env.get "B" "")) (eq? (sum2 "A" "B") 5))
  (test both_zero (mocks (env.get "A" "0") (env.get "B" "0")) (eq? (sum2 "A" "B") 0)))""")

add("cap", "env_lower_compare",
    "Case-insensitive equality between env value and a literal.",
    """(module EnvLowerCmp
  (extern defun env_read (k) @capability "env.get")
  (defun ieq (k expected)
    (if (str_eq (str_lower (env_read k)) (str_lower expected)) 1 0))
  (test exact (mocks (env.get "MODE" "production")) (eq? (ieq "MODE" "production") 1))
  (test mix (mocks (env.get "MODE" "PROD")) (eq? (ieq "MODE" "prod") 1))
  (test diff (mocks (env.get "MODE" "dev")) (eq? (ieq "MODE" "prod") 0)))""")

# ============== capability: file ==============

add("cap", "file_word_count_simple",
    "Count whitespace-separated words in a file.",
    """(module FileWords
  (extern defun file_read (p) @capability "file.read")
  (defun wc (p)
    (def c (file_read p))
    (if (str_eq c "") (return 0))
    (arr.length (str_split c " ")))
  (test empty (mocks (file.read "/a" "")) (eq? (wc "/a") 0))
  (test three (mocks (file.read "/a" "the cat sat")) (eq? (wc "/a") 3))
  (test one (mocks (file.read "/a" "hello")) (eq? (wc "/a") 1)))""")

add("cap", "file_uppercase_content",
    "Read file and return its content uppercased.",
    """(module FileUp
  (extern defun file_read (p) @capability "file.read")
  (defun up (p)
    (str_upper (file_read p)))
  (test empty (mocks (file.read "/a" "")) (eq? (up "/a") ""))
  (test mixed (mocks (file.read "/a" "Hello")) (eq? (up "/a") "HELLO"))
  (test already (mocks (file.read "/a" "ABC")) (eq? (up "/a") "ABC")))""")

add("cap", "file_starts_with_hash",
    "Return 1 if file begins with '#', else 0; empty file returns 0.",
    """(module FileHash
  (extern defun file_read (p) @capability "file.read")
  (defun begins_hash (p)
    (def c (file_read p))
    (if (str_eq c "") (return 0))
    (if (str_eq (str_substring c 0 1) "#") 1 0))
  (test empty (mocks (file.read "/a" "")) (eq? (begins_hash "/a") 0))
  (test yes (mocks (file.read "/a" "# header")) (eq? (begins_hash "/a") 1))
  (test no (mocks (file.read "/a" "hello")) (eq? (begins_hash "/a") 0)))""")

add("cap", "file_min_words_5",
    "Return 1 if file has at least 5 whitespace-separated words.",
    """(module FileMinWords
  (extern defun file_read (p) @capability "file.read")
  (defun has5 (p)
    (def c (file_read p))
    (if (str_eq c "") (return 0))
    (if (>= (arr.length (str_split c " ")) 5) 1 0))
  (test short (mocks (file.read "/a" "a b c")) (eq? (has5 "/a") 0))
  (test exact (mocks (file.read "/a" "a b c d e")) (eq? (has5 "/a") 1))
  (test more (mocks (file.read "/a" "one two three four five six")) (eq? (has5 "/a") 1))
  (test empty (mocks (file.read "/a" "")) (eq? (has5 "/a") 0)))""")

add("cap", "file_byte_doubled",
    "Return 2 * length of the file's content.",
    """(module FileByteDbl
  (extern defun file_read (p) @capability "file.read")
  (defun bdbl (p)
    (* 2 (str_length (file_read p))))
  (test empty (mocks (file.read "/a" "")) (eq? (bdbl "/a") 0))
  (test small (mocks (file.read "/a" "hi")) (eq? (bdbl "/a") 4))
  (test big (mocks (file.read "/a" "abcdefghij")) (eq? (bdbl "/a") 20)))""")

add("cap", "file_cat_two",
    "Concatenate contents of two files with a newline between.",
    """(module FileCat2
  (extern defun file_read (p) @capability "file.read")
  (defun cat2 (a b)
    (str_concat (str_concat (file_read a) "\\n") (file_read b)))
  (test both (mocks (file.read "/a" "first") (file.read "/b" "second")) (eq? (cat2 "/a" "/b") "first\\nsecond"))
  (test empty_a (mocks (file.read "/a" "") (file.read "/b" "x")) (eq? (cat2 "/a" "/b") "\\nx")))""")

add("cap", "file_first_word",
    "Return the first whitespace-separated word from a file, '' if empty.",
    """(module FileFirstWord
  (extern defun file_read (p) @capability "file.read")
  (defun first_word (p)
    (def c (file_read p))
    (if (str_eq c "") (return ""))
    (arr.get (str_split c " ") 0))
  (test empty (mocks (file.read "/a" "")) (eq? (first_word "/a") ""))
  (test single (mocks (file.read "/a" "hello")) (eq? (first_word "/a") "hello"))
  (test multi (mocks (file.read "/a" "the quick brown")) (eq? (first_word "/a") "the")))""")

# ============== capability: http ==============

add("cap", "http_body_doubled_len",
    "Fetch URL and return 2x its body length.",
    """(module HttpBodyDbl
  (extern defun http_get (u) @capability "http.fetch")
  (defun bdbl (u)
    (* 2 (str_length (http_get u))))
  (test empty (mocks (http.fetch "http://x" "")) (eq? (bdbl "http://x") 0))
  (test ok (mocks (http.fetch "http://x" "OK")) (eq? (bdbl "http://x") 4))
  (test bigger (mocks (http.fetch "http://x" "hello world")) (eq? (bdbl "http://x") 22)))""")

add("cap", "http_body_uppercase_first3",
    "Take first 3 chars of body, uppercased; empty body returns ''.",
    """(module HttpUpper3
  (extern defun http_get (u) @capability "http.fetch")
  (defun u3 (u)
    (def b (http_get u))
    (if (str_eq b "") (return ""))
    (str_upper (str_substring b 0 3)))
  (test empty (mocks (http.fetch "http://x" "")) (eq? (u3 "http://x") ""))
  (test long (mocks (http.fetch "http://x" "hello world")) (eq? (u3 "http://x") "HEL"))
  (test exact3 (mocks (http.fetch "http://x" "abc")) (eq? (u3 "http://x") "ABC")))""")

add("cap", "http_body_word_count",
    "Count words in HTTP body (space-separated).",
    """(module HttpWords
  (extern defun http_get (u) @capability "http.fetch")
  (defun wc (u)
    (def b (http_get u))
    (if (str_eq b "") (return 0))
    (arr.length (str_split b " ")))
  (test empty (mocks (http.fetch "http://x" "")) (eq? (wc "http://x") 0))
  (test five (mocks (http.fetch "http://x" "the quick brown fox jumps")) (eq? (wc "http://x") 5))
  (test one (mocks (http.fetch "http://x" "hello")) (eq? (wc "http://x") 1)))""")

add("cap", "http_body_starts_with",
    "Return 1 iff HTTP body begins with the literal '<html>'.",
    """(module HttpStartsHtml
  (extern defun http_get (u) @capability "http.fetch")
  (defun is_html (u)
    (def b (http_get u))
    (if (< (str_length b) 6) (return 0))
    (if (str_eq (str_substring b 0 6) "<html>") 1 0))
  (test yes (mocks (http.fetch "http://x" "<html><body>hi")) (eq? (is_html "http://x") 1))
  (test no (mocks (http.fetch "http://x" "hello world")) (eq? (is_html "http://x") 0))
  (test short (mocks (http.fetch "http://x" "no")) (eq? (is_html "http://x") 0)))""")

add("cap", "http_concat_two_urls",
    "Fetch two URLs and concatenate the bodies.",
    """(module HttpConcat
  (extern defun http_get (u) @capability "http.fetch")
  (defun cat (a b)
    (str_concat (http_get a) (http_get b)))
  (test both (mocks (http.fetch "http://a" "AA") (http.fetch "http://b" "BB")) (eq? (cat "http://a" "http://b") "AABB"))
  (test empty (mocks (http.fetch "http://a" "") (http.fetch "http://b" "x")) (eq? (cat "http://a" "http://b") "x")))""")

add("cap", "http_body_classify_size",
    "Classify body length: empty ('none'), <10 ('small'), else ('big').",
    """(module HttpClassify
  (extern defun http_get (u) @capability "http.fetch")
  (defun cls (u)
    (def n (str_length (http_get u)))
    (if (= n 0) "none" (if (< n 10) "small" "big")))
  (test none (mocks (http.fetch "http://x" "")) (eq? (cls "http://x") "none"))
  (test small (mocks (http.fetch "http://x" "abc")) (eq? (cls "http://x") "small"))
  (test big (mocks (http.fetch "http://x" "abcdefghijklmno")) (eq? (cls "http://x") "big")))""")

# ============== HOF — more ==============

add("pure", "hof_arr_map_negate",
    "Negate every element with arr.map and check sum.",
    """(module HofMapNeg
  (defun neg (x) (- 0 x))
  (defun add2 (a b) (+ a b))
  (test t (do
    (def a : (Array Num) (arr.new 4))
    (arr.set a 0 1) (arr.set a 1 2) (arr.set a 2 3) (arr.set a 3 4)
    (def n : (Array Num) (arr.map a neg))
    (assert-eq (arr.reduce n add2 0) -10))))""")

add("pure", "hof_filter_then_count",
    "Filter for divisible-by-3 and count results.",
    """(module HofDiv3
  (defun div3 (x) (if (= (math.mod x 3) 0) 1 0))
  (test t (do
    (def a : (Array Num) (arr.new 6))
    (arr.set a 0 1) (arr.set a 1 3) (arr.set a 2 6) (arr.set a 3 7) (arr.set a 4 9) (arr.set a 5 2)
    (def b : (Array Num) (arr.filter a div3))
    (assert-eq (arr.length b) 3))))""")

add("pure", "hof_reduce_max",
    "Reduce array to find max.",
    """(module HofMax
  (defun mx (a b) (if (> a b) a b))
  (test t (do
    (def a : (Array Num) (arr.new 5))
    (arr.set a 0 3) (arr.set a 1 8) (arr.set a 2 1) (arr.set a 3 6) (arr.set a 4 9)
    (assert-eq (arr.reduce a mx -1000) 9))))""")

add("pure", "hof_reduce_min",
    "Reduce array to find min.",
    """(module HofMin
  (defun mn (a b) (if (< a b) a b))
  (test t (do
    (def a : (Array Num) (arr.new 5))
    (arr.set a 0 3) (arr.set a 1 8) (arr.set a 2 1) (arr.set a 3 6) (arr.set a 4 9)
    (assert-eq (arr.reduce a mn 1000) 1))))""")

add("pure", "hof_count_above_threshold",
    "Count entries above 50 with map+filter+length.",
    """(module HofAbove50
  (defun gt50 (x) (if (> x 50) 1 0))
  (test t (do
    (def a : (Array Num) (arr.new 5))
    (arr.set a 0 10) (arr.set a 1 60) (arr.set a 2 70) (arr.set a 3 30) (arr.set a 4 80)
    (def big : (Array Num) (arr.filter a gt50))
    (assert-eq (arr.length big) 3))))""")

add("pure", "hof_double_then_filter_above_5",
    "Double then keep > 5 and sum.",
    """(module HofDoubleFilter
  (defun dbl (x) (* x 2))
  (defun gt5 (x) (if (> x 5) 1 0))
  (defun sum (a b) (+ a b))
  (test t (do
    (def a : (Array Num) (arr.new 5))
    (arr.set a 0 1) (arr.set a 1 2) (arr.set a 2 3) (arr.set a 3 4) (arr.set a 4 5)
    (def m : (Array Num) (arr.map a dbl))
    (def f : (Array Num) (arr.filter m gt5))
    (assert-eq (arr.reduce f sum 0) 24))))""")

add("pure", "hof_filter_then_double_then_max",
    "Filter > 0, then double, then take max.",
    """(module HofFilterDoubleMax
  (defun pos (x) (if (> x 0) 1 0))
  (defun dbl (x) (* x 2))
  (defun mx (a b) (if (> a b) a b))
  (test t (do
    (def a : (Array Num) (arr.new 5))
    (arr.set a 0 -3) (arr.set a 1 4) (arr.set a 2 -1) (arr.set a 3 7) (arr.set a 4 2)
    (def f : (Array Num) (arr.filter a pos))
    (def d : (Array Num) (arr.map f dbl))
    (assert-eq (arr.reduce d mx -1000) 14))))""")

add("pure", "hof_square_then_average",
    "Square each, sum, divide by count to get mean of squares.",
    """(module HofMeanSquares
  (defun sq (x) (* x x))
  (defun s2 (a b) (+ a b))
  (defun mean_sq (a n)
    (def m : (Array Num) (arr.map a sq))
    (/ (arr.reduce m s2 0) n))
  (test t (do
    (def a : (Array Num) (arr.new 3))
    (arr.set a 0 2) (arr.set a 1 2) (arr.set a 2 2)
    (assert-eq (mean_sq a 3) 4))))""")

# ============== multi-function pipelines ==============

add("multi", "pipe_clip_then_sum",
    "Clip array values to [0, 10] in-place, then sum.",
    """(module ClipSum
  (defun clip (a n lo hi)
    (def i 0)
    (while (< i n)
      (do
        (if (< (arr.get a i) lo) (arr.set a i lo) 0)
        (if (> (arr.get a i) hi) (arr.set a i hi) 0)
        (set i (+ i 1))))
    a)
  (defun sumv (a n)
    (def s 0)
    (def i 0)
    (while (< i n) (do (set s (+ s (arr.get a i))) (set i (+ i 1))))
    s)
  (test t (do
    (def a : (Array Num) (arr.new 5))
    (arr.set a 0 -5) (arr.set a 1 3) (arr.set a 2 15) (arr.set a 3 8) (arr.set a 4 12)
    (clip a 5 0 10)
    (assert-eq (sumv a 5) 31))))""")

add("multi", "pipe_normalize_to_sum",
    "Normalize so values sum to 1.0; check first element.",
    """(module Normalize
  (defun sumv (a n)
    (def s 0.0)
    (def i 0)
    (while (< i n) (do (set s (+ s (arr.get a i))) (set i (+ i 1))))
    s)
  (defun normalize (a n)
    (def total (sumv a n))
    (def i 0)
    (while (< i n) (do (arr.set a i (/ (arr.get a i) total)) (set i (+ i 1))))
    a)
  (test t (do
    (def a : (Array Num) (arr.new 4))
    (arr.set a 0 1.0) (arr.set a 1 2.0) (arr.set a 2 3.0) (arr.set a 3 4.0)
    (normalize a 4)
    (assert-near (arr.get a 0) 0.1 0.0001)
    (assert-near (sumv a 4) 1.0 0.0001))))""")

add("multi", "pipe_validate_then_lookup",
    "Validate code in [1..3], then route to category name via two helpers.",
    """(module ValidateLookup
  (defun valid (n) (if (and (>= n 1) (<= n 3)) 1 0))
  (defun name (n)
    (if (= n 1) "alpha" (if (= n 2) "beta" (if (= n 3) "gamma" "?"))))
  (defun resolve (n) (if (= (valid n) 1) (name n) "invalid"))
  (test ok1 (eq? (resolve 1) "alpha"))
  (test ok3 (eq? (resolve 3) "gamma"))
  (test bad (eq? (resolve 9) "invalid")))""")

add("multi", "pipe_compute_total_then_tax_then_tip",
    "Subtotal -> tax -> tip pipeline.",
    """(module TotalTaxTip
  (defun subtotal (price qty) (* price qty))
  (defun with_tax (s rate) (* s (+ 1.0 rate)))
  (defun add_tip (t pct) (* t (+ 1.0 pct)))
  (defun final (price qty tax tip)
    (add_tip (with_tax (subtotal price qty) tax) tip))
  (test base (near? (final 10.0 2.0 0.0 0.0) 20.0 0.001))
  (test taxed (near? (final 10.0 2.0 0.10 0.0) 22.0 0.001))
  (test tipped (near? (final 10.0 2.0 0.10 0.20) 26.4 0.001)))""")

add("multi", "pipe_count_then_avg_then_class",
    "Count items, average, then bucket as 'low'/'mid'/'high'.",
    """(module CountAvgClass
  (defun sumv (a n)
    (def s 0.0)
    (def i 0)
    (while (< i n) (do (set s (+ s (arr.get a i))) (set i (+ i 1))))
    s)
  (defun avg (a n) (/ (sumv a n) n))
  (defun cls (m)
    (if (< m 30.0) "low" (if (< m 70.0) "mid" "high")))
  (defun classify_arr (a n) (cls (avg a n)))
  (test low (do
    (def a : (Array Num) (arr.new 3))
    (arr.set a 0 10.0) (arr.set a 1 20.0) (arr.set a 2 15.0)
    (assert-eq (classify_arr a 3) "low")))
  (test mid (do
    (def a : (Array Num) (arr.new 3))
    (arr.set a 0 40.0) (arr.set a 1 50.0) (arr.set a 2 60.0)
    (assert-eq (classify_arr a 3) "mid")))
  (test high (do
    (def a : (Array Num) (arr.new 2))
    (arr.set a 0 80.0) (arr.set a 1 100.0)
    (assert-eq (classify_arr a 2) "high"))))""")

add("multi", "pipe_centered_z_first",
    "Subtract mean from each, divide by stddev; check first element approx.",
    """(module Zscore
  (defun mean (a n)
    (def s 0.0) (def i 0)
    (while (< i n) (do (set s (+ s (arr.get a i))) (set i (+ i 1))))
    (/ s n))
  (defun stddev (a n mu)
    (def s 0.0) (def i 0)
    (while (< i n)
      (do
        (def d (- (arr.get a i) mu))
        (set s (+ s (* d d)))
        (set i (+ i 1))))
    (math.sqrt (/ s n)))
  (defun z_first (a n)
    (def mu (mean a n))
    (def sd (stddev a n mu))
    (/ (- (arr.get a 0) mu) sd))
  (test t (do
    (def a : (Array Num) (arr.new 5))
    (arr.set a 0 2.0) (arr.set a 1 4.0) (arr.set a 2 4.0) (arr.set a 3 4.0) (arr.set a 4 6.0)
    (assert-near (z_first a 5) -1.5811388 0.0001))))""")

add("multi", "pipe_filter_then_sort_then_first",
    "Filter > 0, bubble-sort ascending, return smallest (first).",
    """(module FilterSortFirst
  (defun keep_pos (a n)
    (def i 0)
    (def out : (Array Num) (arr.new n))
    (def k 0)
    (while (< i n)
      (do
        (if (> (arr.get a i) 0) (do (arr.set out k (arr.get a i)) (set k (+ k 1))) 0)
        (set i (+ i 1))))
    out)
  (defun count_pos (a n)
    (def c 0)
    (def i 0)
    (while (< i n)
      (do
        (if (> (arr.get a i) 0) (set c (+ c 1)) 0)
        (set i (+ i 1))))
    c)
  (defun bsort (a m)
    (def i 0)
    (while (< i (- m 1))
      (do
        (def j 0)
        (while (< j (- m (+ i 1)))
          (do
            (def x (arr.get a j))
            (def y (arr.get a (+ j 1)))
            (if (> x y)
              (do (arr.set a j y) (arr.set a (+ j 1) x))
              0)
            (set j (+ j 1))))
        (set i (+ i 1))))
    a)
  (defun smallest_pos (a n)
    (def filt : (Array Num) (keep_pos a n))
    (def m (count_pos a n))
    (bsort filt m)
    (arr.get filt 0))
  (test t (do
    (def a : (Array Num) (arr.new 6))
    (arr.set a 0 -1) (arr.set a 1 5) (arr.set a 2 -3) (arr.set a 3 2) (arr.set a 4 -7) (arr.set a 5 8)
    (assert-eq (smallest_pos a 6) 2))))""")

# ============== more pure ==============

add("pure", "leap_year_check",
    "Gregorian leap year: divisible by 4 and not by 100 unless by 400.",
    """(module Leap
  (defun is_leap (y)
    (if (= (math.mod y 400) 0) 1
      (if (= (math.mod y 100) 0) 0
        (if (= (math.mod y 4) 0) 1 0))))
  (test y2000 (eq? (is_leap 2000) 1))
  (test y2024 (eq? (is_leap 2024) 1))
  (test y1900 (eq? (is_leap 1900) 0))
  (test y2023 (eq? (is_leap 2023) 0)))""")

add("pure", "fizzbuzz_word",
    "Single-step FizzBuzz for one number n.",
    """(module Fizzbuzz1
  (defun fb (n)
    (if (= (math.mod n 15) 0) "FizzBuzz"
      (if (= (math.mod n 3) 0) "Fizz"
        (if (= (math.mod n 5) 0) "Buzz" (str.from_num n)))))
  (test fb (eq? (fb 3) "Fizz"))
  (test bz (eq? (fb 10) "Buzz"))
  (test fbz (eq? (fb 15) "FizzBuzz"))
  (test plain (eq? (fb 4) "4")))""")

add("pure", "gcd_recursive",
    "Greatest common divisor via Euclid's algorithm (recursive).",
    """(module GcdRec
  (defun gcd (a b)
    (if (= b 0) a (gcd b (math.mod a b))))
  (test simple (eq? (gcd 12 8) 4))
  (test coprime (eq? (gcd 7 11) 1))
  (test same (eq? (gcd 9 9) 9))
  (test zero (eq? (gcd 12 0) 12)))""")

add("pure", "lcm_via_gcd",
    "Least common multiple via gcd.",
    """(module LcmGcd
  (defun gcd (a b) (if (= b 0) a (gcd b (math.mod a b))))
  (defun lcm (a b) (/ (* a b) (gcd a b)))
  (test simple (eq? (lcm 4 6) 12))
  (test coprime (eq? (lcm 7 5) 35))
  (test one (eq? (lcm 1 9) 9)))""")

add("pure", "fibonacci_iter",
    "Iterative Fibonacci F(n).",
    """(module FibIter
  (defun fib (n)
    (if (= n 0) (return 0))
    (def a 0) (def b 1) (def i 1)
    (while (< i n)
      (do
        (def t (+ a b))
        (set a b)
        (set b t)
        (set i (+ i 1))))
    b)
  (test f0 (eq? (fib 0) 0))
  (test f1 (eq? (fib 1) 1))
  (test f10 (eq? (fib 10) 55))
  (test f15 (eq? (fib 15) 610)))""")

add("pure", "is_prime_sqrt_loop",
    "Trial division up to sqrt; n<2 → 0.",
    """(module Prime
  (defun is_prime (n)
    (if (< n 2) (return 0))
    (if (= n 2) (return 1))
    (if (= (math.mod n 2) 0) (return 0))
    (def i 3)
    (def found 1)
    (while (<= (* i i) n)
      (do
        (if (= (math.mod n i) 0) (set found 0) 0)
        (set i (+ i 2))))
    found)
  (test n2 (eq? (is_prime 2) 1))
  (test n3 (eq? (is_prime 3) 1))
  (test n4 (eq? (is_prime 4) 0))
  (test n17 (eq? (is_prime 17) 1))
  (test n100 (eq? (is_prime 100) 0))
  (test n1 (eq? (is_prime 1) 0)))""")

add("pure", "collatz_steps",
    "Steps for n to reach 1 via Collatz.",
    """(module CollatzN
  (defun csteps (n)
    (def x n) (def k 0)
    (while (> x 1)
      (do
        (if (= (math.mod x 2) 0) (set x (/ x 2)) (set x (+ (* 3 x) 1)))
        (set k (+ k 1))))
    k)
  (test one (eq? (csteps 1) 0))
  (test two (eq? (csteps 2) 1))
  (test seven (eq? (csteps 7) 16)))""")

add("pure", "binary_search_in_sorted",
    "Binary search; return index or -1.",
    """(module BinSearch
  (defun bsearch (a n target)
    (def lo 0) (def hi (- n 1)) (def r -1) (def done 0)
    (while (= done 0)
      (do
        (if (> lo hi) (set done 1)
          (do
            (def mid (math.floor (/ (+ lo hi) 2)))
            (def v (arr.get a mid))
            (if (= v target) (do (set r mid) (set done 1))
              (if (< v target) (set lo (+ mid 1)) (set hi (- mid 1))))))))
    r)
  (test found (do
    (def a : (Array Num) (arr.new 6))
    (arr.set a 0 1) (arr.set a 1 3) (arr.set a 2 5) (arr.set a 3 7) (arr.set a 4 9) (arr.set a 5 11)
    (assert-eq (bsearch a 6 7) 3)))
  (test miss (do
    (def a : (Array Num) (arr.new 6))
    (arr.set a 0 1) (arr.set a 1 3) (arr.set a 2 5) (arr.set a 3 7) (arr.set a 4 9) (arr.set a 5 11)
    (assert-eq (bsearch a 6 8) -1))))""")

add("pure", "selection_sort_inplace",
    "Selection sort in place; check first/last.",
    """(module SelectionSort
  (defun ssort (a n)
    (def i 0)
    (while (< i (- n 1))
      (do
        (def mn i)
        (def j (+ i 1))
        (while (< j n)
          (do
            (if (< (arr.get a j) (arr.get a mn)) (set mn j) 0)
            (set j (+ j 1))))
        (def t (arr.get a i))
        (arr.set a i (arr.get a mn))
        (arr.set a mn t)
        (set i (+ i 1))))
    a)
  (test t (do
    (def a : (Array Num) (arr.new 5))
    (arr.set a 0 5) (arr.set a 1 2) (arr.set a 2 9) (arr.set a 3 1) (arr.set a 4 4)
    (ssort a 5)
    (assert-eq (arr.get a 0) 1)
    (assert-eq (arr.get a 4) 9))))""")

add("pure", "insertion_sort_inplace",
    "Insertion sort in place; verify ascending result.",
    """(module InsertionSort
  (defun isort (a n)
    (def i 1)
    (while (< i n)
      (do
        (def key (arr.get a i))
        (def j (- i 1))
        (def keep_going 1)
        (while (= keep_going 1)
          (do
            (if (< j 0) (set keep_going 0)
              (if (<= (arr.get a j) key) (set keep_going 0)
                (do
                  (arr.set a (+ j 1) (arr.get a j))
                  (set j (- j 1)))))))
        (arr.set a (+ j 1) key)
        (set i (+ i 1))))
    a)
  (test t (do
    (def a : (Array Num) (arr.new 5))
    (arr.set a 0 4) (arr.set a 1 1) (arr.set a 2 3) (arr.set a 3 5) (arr.set a 4 2)
    (isort a 5)
    (assert-eq (arr.get a 0) 1)
    (assert-eq (arr.get a 1) 2)
    (assert-eq (arr.get a 4) 5))))""")

add("pure", "ackermann_small",
    "Small Ackermann A(m,n) for m<=2, n<=4.",
    """(module Ack
  (defun ack (m n)
    (if (= m 0) (return (+ n 1)))
    (if (= n 0) (return (ack (- m 1) 1)))
    (ack (- m 1) (ack m (- n 1))))
  (test a00 (eq? (ack 0 0) 1))
  (test a13 (eq? (ack 1 3) 5))
  (test a22 (eq? (ack 2 2) 7))
  (test a23 (eq? (ack 2 3) 9)))""")

add("pure", "power_recursive",
    "Compute base^exp recursively (exp>=0).",
    """(module PowRec
  (defun powr (b e)
    (if (= e 0) (return 1))
    (* b (powr b (- e 1))))
  (test p0 (eq? (powr 2 0) 1))
  (test p3 (eq? (powr 2 10) 1024))
  (test p5 (eq? (powr 3 5) 243)))""")

add("pure", "digit_sum_recursive",
    "Sum digits of non-negative integer recursively.",
    """(module DigitSum
  (defun dsum (n)
    (if (< n 10) (return n))
    (+ (math.mod n 10) (dsum (/ (- n (math.mod n 10)) 10))))
  (test small (eq? (dsum 9) 9))
  (test mid (eq? (dsum 123) 6))
  (test big (eq? (dsum 9999) 36))
  (test zero (eq? (dsum 0) 0)))""")

add("pure", "digital_root",
    "Repeatedly take digit sum until single digit.",
    """(module DigitalRoot
  (defun dsum (n)
    (def s 0) (def x n)
    (while (> x 0)
      (do
        (set s (+ s (math.mod x 10)))
        (set x (/ (- x (math.mod x 10)) 10))))
    s)
  (defun droot (n)
    (def x n)
    (while (>= x 10) (set x (dsum x)))
    x)
  (test small (eq? (droot 38) 2))
  (test single (eq? (droot 7) 7))
  (test big (eq? (droot 9875) 2)))""")


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
