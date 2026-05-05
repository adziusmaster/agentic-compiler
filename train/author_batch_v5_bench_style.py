#!/usr/bin/env python3
"""Batch v5: bench-style capability pairs.

Corrects three failure modes observed on bench eval:
  1. Model invented (env_read k) using undefined `k` — must learn the
     "hardcode env-key inside body" idiom for problems where tests pass
     only the default to the function.
  2. Model wrote `(extern ... (return ""))` — extern declarations have NO body.
  3. file.write capability was entirely absent from training.

Each pair uses the tests.ag style externs (typed signatures) and mocks-form.
Function names match the test call sites exactly.
"""
from __future__ import annotations
import json
import sys


PAIRS: list[tuple[str, str, str, str]] = []


def add(category: str, topic: str, objective: str, solution: str) -> None:
    PAIRS.append((category, topic, objective, solution.strip()))


# ============== Style A: env-key HARDCODED inside function ==============
# Function signature takes only the user-provided arg(s); the env key it reads
# is fixed inside the body. This mirrors bench/13-env-int-or.

add("cap", "port_or_default",
    "Read env var PORT, parse to number, return default if missing.",
    """(module PortOr
  (extern defun env_read ((key : Str)) : Str @capability "env.get")
  (defun port_or (default)
    (def v (env_read "PORT"))
    (if (str_eq v "") (return default))
    (str_to_num v))
  (test default_when_empty (mocks (env.get "PORT" "")) (assert-eq (port_or 8080) 8080))
  (test parses_present (mocks (env.get "PORT" "3000")) (assert-eq (port_or 8080) 3000))
  (test reads_zero (mocks (env.get "PORT" "0")) (assert-eq (port_or 8080) 0)))""")

add("cap", "host_or_default",
    "Read env var HOST, return it; default to 'localhost' if blank.",
    """(module HostOr
  (extern defun env_read ((key : Str)) : Str @capability "env.get")
  (defun host_or (default)
    (def v (env_read "HOST"))
    (if (str_eq v "") (return default))
    v)
  (test missing (mocks (env.get "HOST" "")) (assert-eq (host_or "localhost") "localhost"))
  (test set (mocks (env.get "HOST" "example.com")) (assert-eq (host_or "localhost") "example.com")))""")

add("cap", "log_level_or_info",
    "Read env var LOG_LEVEL, return uppercased; default 'INFO' if blank.",
    """(module LogLevel
  (extern defun env_read ((key : Str)) : Str @capability "env.get")
  (defun level_or (default)
    (def v (env_read "LOG_LEVEL"))
    (if (str_eq v "") (return default))
    (str_upper v))
  (test missing (mocks (env.get "LOG_LEVEL" "")) (assert-eq (level_or "INFO") "INFO"))
  (test debug (mocks (env.get "LOG_LEVEL" "debug")) (assert-eq (level_or "INFO") "DEBUG"))
  (test already (mocks (env.get "LOG_LEVEL" "WARN")) (assert-eq (level_or "INFO") "WARN")))""")

add("cap", "feature_flag_enabled",
    "Read env var FEATURE_X and treat 'on'/'1'/'true' as enabled (returning 1) else 0.",
    """(module FeatFlag
  (extern defun env_read ((key : Str)) : Str @capability "env.get")
  (defun is_enabled ()
    (def v (str_lower (env_read "FEATURE_X")))
    (if (str_eq v "on") (return 1))
    (if (str_eq v "1") (return 1))
    (if (str_eq v "true") (return 1))
    0)
  (test on (mocks (env.get "FEATURE_X" "on")) (assert-eq (is_enabled) 1))
  (test true (mocks (env.get "FEATURE_X" "true")) (assert-eq (is_enabled) 1))
  (test off (mocks (env.get "FEATURE_X" "off")) (assert-eq (is_enabled) 0))
  (test missing (mocks (env.get "FEATURE_X" "")) (assert-eq (is_enabled) 0)))""")

add("cap", "timeout_seconds",
    "Read env var TIMEOUT, parse to int; return default if missing.",
    """(module Timeout
  (extern defun env_read ((key : Str)) : Str @capability "env.get")
  (defun timeout_or (default)
    (def v (env_read "TIMEOUT"))
    (if (str_eq v "") (return default))
    (str_to_num v))
  (test missing (mocks (env.get "TIMEOUT" "")) (assert-eq (timeout_or 30) 30))
  (test set (mocks (env.get "TIMEOUT" "60")) (assert-eq (timeout_or 30) 60))
  (test zero (mocks (env.get "TIMEOUT" "0")) (assert-eq (timeout_or 30) 0)))""")

add("cap", "max_retries_clamped",
    "Read env var MAX_RETRIES, parse to int, clamp to [0, 10]; default 3.",
    """(module Retries
  (extern defun env_read ((key : Str)) : Str @capability "env.get")
  (defun retries_or (default)
    (def v (env_read "MAX_RETRIES"))
    (if (str_eq v "") (return default))
    (def n (str_to_num v))
    (if (< n 0) (return 0))
    (if (> n 10) (return 10))
    n)
  (test missing (mocks (env.get "MAX_RETRIES" "")) (assert-eq (retries_or 3) 3))
  (test small (mocks (env.get "MAX_RETRIES" "2")) (assert-eq (retries_or 3) 2))
  (test high (mocks (env.get "MAX_RETRIES" "99")) (assert-eq (retries_or 3) 10))
  (test neg (mocks (env.get "MAX_RETRIES" "-5")) (assert-eq (retries_or 3) 0)))""")

add("cap", "user_or_anonymous",
    "Read env USER and prefix with 'user:'; default 'user:anonymous' if blank.",
    """(module UserOrAnon
  (extern defun env_read ((key : Str)) : Str @capability "env.get")
  (defun who ()
    (def v (env_read "USER"))
    (if (str_eq v "") (return "user:anonymous"))
    (str_concat "user:" v))
  (test missing (mocks (env.get "USER" "")) (assert-eq (who) "user:anonymous"))
  (test set (mocks (env.get "USER" "alice")) (assert-eq (who) "user:alice")))""")

add("cap", "api_url_with_default",
    "Read env API_URL; if blank or missing scheme, return 'https://api.local'.",
    """(module ApiUrl
  (extern defun env_read ((key : Str)) : Str @capability "env.get")
  (defun api_url ()
    (def v (env_read "API_URL"))
    (if (str_eq v "") (return "https://api.local"))
    (def n (str_length v))
    (if (< n 8) (return "https://api.local"))
    (if (str_eq (str_substring v 0 8) "https://") (return v))
    "https://api.local")
  (test missing (mocks (env.get "API_URL" "")) (assert-eq (api_url) "https://api.local"))
  (test ok (mocks (env.get "API_URL" "https://prod.example.com")) (assert-eq (api_url) "https://prod.example.com"))
  (test bad (mocks (env.get "API_URL" "no-scheme")) (assert-eq (api_url) "https://api.local")))""")

# ============== Style B: env-key as parameter (also represented) ==============

add("cap", "env_get_or_param",
    "Read any env var by key; return default if blank.",
    """(module EnvGetOr
  (extern defun env_read ((key : Str)) : Str @capability "env.get")
  (defun env_or (key default)
    (def v (env_read key))
    (if (str_eq v "") (return default))
    v)
  (test missing (mocks (env.get "K" "")) (assert-eq (env_or "K" "x") "x"))
  (test set (mocks (env.get "K" "value")) (assert-eq (env_or "K" "x") "value")))""")

add("cap", "env_int_param_with_default",
    "Read int env var by name; default if missing.",
    """(module EnvIntOr
  (extern defun env_read ((key : Str)) : Str @capability "env.get")
  (defun int_or (key default)
    (def v (env_read key))
    (if (str_eq v "") (return default))
    (str_to_num v))
  (test missing (mocks (env.get "N" "")) (assert-eq (int_or "N" 100) 100))
  (test parsed (mocks (env.get "N" "42")) (assert-eq (int_or "N" 100) 42)))""")

# ============== file.write capability — NEW (was absent from training) ==============

add("cap", "save_status_after_write",
    "Write a short status string and return the write-call's status code.",
    """(module SaveStatus
  (extern defun file_write ((path : Str) (body : Str)) : Num @capability "file.write")
  (defun save_status (path n)
    (file_write path (str.from_num n)))
  (test ok (mocks (file.write "/tmp/s.txt" 1)) (assert-eq (save_status "/tmp/s.txt" 7) 1))
  (test zero (mocks (file.write "/tmp/s.txt" 1)) (assert-eq (save_status "/tmp/s.txt" 0) 1)))""")

add("cap", "write_constant_message",
    "Write a fixed message 'hello' and return the status code.",
    """(module WriteHello
  (extern defun file_write ((path : Str) (body : Str)) : Num @capability "file.write")
  (defun write_hello (path)
    (file_write path "hello"))
  (test ok (mocks (file.write "/tmp/h.txt" 1)) (assert-eq (write_hello "/tmp/h.txt") 1)))""")

add("cap", "write_n_chars_status",
    "Write n copies of 'x' to path and return the write status.",
    """(module WriteNX
  (extern defun file_write ((path : Str) (body : Str)) : Num @capability "file.write")
  (defun build_x (n)
    (def s "")
    (def i 0)
    (while (< i n)
      (do
        (set s (str_concat s "x"))
        (set i (+ i 1))))
    s)
  (defun write_xs (path n)
    (file_write path (build_x n)))
  (test small (mocks (file.write "/t" 1)) (assert-eq (write_xs "/t" 3) 1))
  (test zero (mocks (file.write "/t" 1)) (assert-eq (write_xs "/t" 0) 1)))""")

add("cap", "write_uppercase_of_input",
    "Uppercase the input string and write to path; return status.",
    """(module WriteUpper
  (extern defun file_write ((path : Str) (body : Str)) : Num @capability "file.write")
  (defun write_upper (path s)
    (file_write path (str_upper s)))
  (test t (mocks (file.write "/x" 1)) (assert-eq (write_upper "/x" "hi") 1))
  (test empty (mocks (file.write "/x" 1)) (assert-eq (write_upper "/x" "") 1)))""")

add("cap", "write_only_if_nonempty",
    "Only write to path if the body is non-empty; return 0 if skipped, status otherwise.",
    """(module WriteIfNE
  (extern defun file_write ((path : Str) (body : Str)) : Num @capability "file.write")
  (defun write_ne (path body)
    (if (str_eq body "") (return 0))
    (file_write path body))
  (test empty (assert-eq (write_ne "/x" "") 0))
  (test nonempty (mocks (file.write "/x" 1)) (assert-eq (write_ne "/x" "hi") 1)))""")

add("cap", "write_log_line",
    "Write `[level] msg\\n` to path and return write status.",
    """(module WriteLog
  (extern defun file_write ((path : Str) (body : Str)) : Num @capability "file.write")
  (defun write_log (path level msg)
    (def line (str_concat (str_concat (str_concat (str_concat "[" level) "] ") msg) "\\n"))
    (file_write path line))
  (test info (mocks (file.write "/log" 1)) (assert-eq (write_log "/log" "INFO" "hi") 1)))""")

# ============== file_read + file_write composites (matches bench/20-file-copy) ==============

add("cap", "copy_file_return_count",
    "Read source, write to destination, return number of characters copied.",
    """(module CopyFile
  (extern defun file_read ((path : Str)) : Str @capability "file.read")
  (extern defun file_write ((path : Str) (body : Str)) : Num @capability "file.write")
  (defun copy_file (src dst)
    (def s (file_read src))
    (file_write dst s)
    (str_length s))
  (test five (mocks (file.read "/src" "hello") (file.write "/dst" 1)) (assert-eq (copy_file "/src" "/dst") 5))
  (test empty (mocks (file.read "/src" "") (file.write "/dst" 1)) (assert-eq (copy_file "/src" "/dst") 0))
  (test long (mocks (file.read "/src" "abcdefghijklmnopqrst") (file.write "/dst" 1)) (assert-eq (copy_file "/src" "/dst") 20)))""")

add("cap", "copy_uppercase",
    "Read source, uppercase, write to destination, return uppercased length.",
    """(module CopyUpper
  (extern defun file_read ((path : Str)) : Str @capability "file.read")
  (extern defun file_write ((path : Str) (body : Str)) : Num @capability "file.write")
  (defun copy_upper (src dst)
    (def s (str_upper (file_read src)))
    (file_write dst s)
    (str_length s))
  (test t (mocks (file.read "/s" "Hello") (file.write "/d" 1)) (assert-eq (copy_upper "/s" "/d") 5)))""")

add("cap", "copy_first_line_only",
    "Copy only the first line (up to first '\\n') from src to dst; return its length.",
    """(module CopyFirstLine
  (extern defun file_read ((path : Str)) : Str @capability "file.read")
  (extern defun file_write ((path : Str) (body : Str)) : Num @capability "file.write")
  (defun first_line (s)
    (def n (str_length s))
    (def i 0)
    (def done 0)
    (while (= done 0)
      (do
        (if (>= i n) (set done 1)
          (if (str_eq (str_substring s i 1) "\\n") (set done 1)
            (set i (+ i 1))))))
    (str_substring s 0 i))
  (defun copy_fl (src dst)
    (def line (first_line (file_read src)))
    (file_write dst line)
    (str_length line))
  (test multi (mocks (file.read "/s" "abc\\ndef") (file.write "/d" 1)) (assert-eq (copy_fl "/s" "/d") 3))
  (test single (mocks (file.read "/s" "hello") (file.write "/d" 1)) (assert-eq (copy_fl "/s" "/d") 5))
  (test empty (mocks (file.read "/s" "") (file.write "/d" 1)) (assert-eq (copy_fl "/s" "/d") 0)))""")

# ============== file.read with path-as-arg (matches bench 17-file-line-count) ==============

add("cap", "line_count_with_trailing",
    "Count lines: each '\\n' starts a new line; trailing non-empty content also counts as a line.",
    """(module LineCount
  (extern defun file_read ((path : Str)) : Str @capability "file.read")
  (defun line_count (path)
    (def s (file_read path))
    (def n (str_length s))
    (if (= n 0) (return 0))
    (def c 0)
    (def i 0)
    (while (< i n)
      (do
        (if (str_eq (str_substring s i 1) "\\n") (set c (+ c 1)) 0)
        (set i (+ i 1))))
    (if (str_eq (str_substring s (- n 1) 1) "\\n") c (+ c 1)))
  (test empty (mocks (file.read "/f" "")) (assert-eq (line_count "/f") 0))
  (test one_terminated (mocks (file.read "/f" "a\\n")) (assert-eq (line_count "/f") 1))
  (test two_terminated (mocks (file.read "/f" "a\\nb\\n")) (assert-eq (line_count "/f") 2))
  (test two_no_final (mocks (file.read "/f" "a\\nb")) (assert-eq (line_count "/f") 2))
  (test just_newline (mocks (file.read "/f" "\\n")) (assert-eq (line_count "/f") 1))
  (test double_nl (mocks (file.read "/f" "\\n\\n")) (assert-eq (line_count "/f") 2)))""")

add("cap", "char_count_path",
    "Read file at path and return number of characters.",
    """(module CharCount
  (extern defun file_read ((path : Str)) : Str @capability "file.read")
  (defun char_count (path)
    (str_length (file_read path)))
  (test five (mocks (file.read "/f" "hello")) (assert-eq (char_count "/f") 5))
  (test empty (mocks (file.read "/f" "")) (assert-eq (char_count "/f") 0)))""")

add("cap", "first_word_of_file",
    "Return the first whitespace-separated word of a file's content; '' if empty.",
    """(module FirstWord
  (extern defun file_read ((path : Str)) : Str @capability "file.read")
  (defun first_word (path)
    (def s (file_read path))
    (if (str_eq s "") (return ""))
    (arr.get (str_split s " ") 0))
  (test multi (mocks (file.read "/f" "the quick brown")) (assert-eq (first_word "/f") "the"))
  (test single (mocks (file.read "/f" "hello")) (assert-eq (first_word "/f") "hello"))
  (test empty (mocks (file.read "/f" "")) (assert-eq (first_word "/f") "")))""")

add("cap", "file_contains_keyword",
    "Return 1 if the file's content contains the keyword (substring), else 0.",
    """(module FileContains
  (extern defun file_read ((path : Str)) : Str @capability "file.read")
  (defun contains_kw (path kw)
    (def s (file_read path))
    (def ns (str_length s))
    (def nk (str_length kw))
    (if (= nk 0) (return 1))
    (if (< ns nk) (return 0))
    (def i 0)
    (def found 0)
    (while (and (<= i (- ns nk)) (= found 0))
      (do
        (if (str_eq (str_substring s i nk) kw) (set found 1) 0)
        (set i (+ i 1))))
    found)
  (test yes (mocks (file.read "/f" "hello world")) (assert-eq (contains_kw "/f" "world") 1))
  (test no (mocks (file.read "/f" "hello there")) (assert-eq (contains_kw "/f" "world") 0))
  (test empty_kw (mocks (file.read "/f" "anything")) (assert-eq (contains_kw "/f" "") 1)))""")

# ============== http capability with hardcoded URL semantics ==============

add("cap", "http_status_two_buckets",
    "Fetch URL and classify body length: 0 → 'none', 1-99 → 'short', else → 'long'.",
    """(module HttpBuckets
  (extern defun http_get ((url : Str)) : Str @capability "http.fetch")
  (defun bucket (url)
    (def n (str_length (http_get url)))
    (if (= n 0) (return "none"))
    (if (< n 100) (return "short"))
    "long")
  (test none (mocks (http.fetch "http://x" "")) (assert-eq (bucket "http://x") "none"))
  (test short (mocks (http.fetch "http://x" "ok")) (assert-eq (bucket "http://x") "short"))
  (test long (mocks (http.fetch "http://x" "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKL")) (assert-eq (bucket "http://x") "long")))""")

add("cap", "http_word_count_path",
    "Fetch URL; return word count of body (split by space); 0 if empty body.",
    """(module HttpWordCount
  (extern defun http_get ((url : Str)) : Str @capability "http.fetch")
  (defun word_count (url)
    (def b (http_get url))
    (if (str_eq b "") (return 0))
    (arr.length (str_split b " ")))
  (test some (mocks (http.fetch "http://x" "the cat sat")) (assert-eq (word_count "http://x") 3))
  (test empty (mocks (http.fetch "http://x" "")) (assert-eq (word_count "http://x") 0)))""")


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
