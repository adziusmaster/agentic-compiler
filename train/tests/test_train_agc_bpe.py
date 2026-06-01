"""Tests for the whitelist filter in train_agc_bpe.py."""
import pytest


def test_keeps_stdlib_calls():
    from train_agc_bpe import is_valid_agc_token, AGC_STDLIB
    assert is_valid_agc_token("str_substring", AGC_STDLIB) is True
    assert is_valid_agc_token("math_floor", AGC_STDLIB) is True
    assert is_valid_agc_token("list_length", AGC_STDLIB) is True


def test_keeps_sexpr_openers():
    from train_agc_bpe import is_valid_agc_token, AGC_STDLIB
    assert is_valid_agc_token("(defun", AGC_STDLIB) is True
    assert is_valid_agc_token("(assert-eq", AGC_STDLIB) is True
    assert is_valid_agc_token("(extern", AGC_STDLIB) is True


def test_keeps_type_annotations():
    from train_agc_bpe import is_valid_agc_token, AGC_STDLIB
    assert is_valid_agc_token(": Num", AGC_STDLIB) is True
    assert is_valid_agc_token("-> Str", AGC_STDLIB) is True


def test_rejects_user_identifiers():
    """User-defined identifiers from the corpus should be rejected."""
    from train_agc_bpe import is_valid_agc_token, AGC_STDLIB
    assert is_valid_agc_token("quarterly_total", AGC_STDLIB) is False
    assert is_valid_agc_token("is_even", AGC_STDLIB) is False
    assert is_valid_agc_token("peak_day", AGC_STDLIB) is False


def test_keeps_short_tokens():
    """Tokens 4 chars or less are kept (likely operators or short keywords)."""
    from train_agc_bpe import is_valid_agc_token, AGC_STDLIB
    assert is_valid_agc_token("eq?", AGC_STDLIB) is True
    assert is_valid_agc_token("def", AGC_STDLIB) is True


def test_rejects_random_byte_fragments():
    from train_agc_bpe import is_valid_agc_token, AGC_STDLIB
    assert is_valid_agc_token("xQz", AGC_STDLIB) is False  # 4 chars but no stdlib match
    assert is_valid_agc_token("abcde_xyz", AGC_STDLIB) is False  # >4 chars, no match
