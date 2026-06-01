"""Curated set of AGC stdlib tokens, keywords, and operators.

Used by train_agc_bpe.py's whitelist filter to keep BPE candidates
that are real AGC syntax and reject corpus-specific user identifiers.
"""

# Core s-expr forms
AGC_KEYWORDS = {
    "module", "defun", "extern", "test", "do", "let", "if", "cond",
    "return", "def", "set!", "call", "lambda",
}

# Type annotations (single tokens after BPE)
AGC_TYPES = {"Num", "Str", "Bool", "List", "Dict", "Unit", "Any"}

# Stdlib calls (snake_case, namespaced)
AGC_STDLIB_CALLS = {
    # Math
    "math_floor", "math_ceil", "math_round", "math_abs", "math_mod",
    "math_min", "math_max", "math_pow", "math_sqrt", "math_sign",
    # String
    "str_split", "str_substring", "str_length", "str_concat", "str_index",
    "str_replace", "str_to_num", "str_to_int", "str_lower", "str_upper",
    "str_trim", "str_starts_with", "str_ends_with", "str_contains",
    # List
    "list_length", "list_get", "list_set", "list_append", "list_concat",
    "list_first", "list_rest", "list_empty?", "list_reverse", "list_map",
    "list_filter", "list_reduce", "list_range", "list_sort",
    # Dict
    "dict_get", "dict_set", "dict_has", "dict_keys", "dict_remove",
    # IO / capability
    "file_read", "file_write", "file_exists", "file_lines",
    "env_get", "env_get_or", "console_print",
    # Comparison / boolean
    "eq?", "lt?", "gt?", "le?", "ge?", "and?", "or?", "not?",
}

# Assertions and test forms
AGC_ASSERTIONS = {
    "assert-eq", "assert-near", "assert-true", "assert-false",
    "eq?", "neq?",
}

# Combined master set
AGC_STDLIB = (
    AGC_KEYWORDS
    | AGC_TYPES
    | AGC_STDLIB_CALLS
    | AGC_ASSERTIONS
)
