"""Tests for extend_qwen_tokenizer.py: dedup + ID assignment for new tokens."""
from pathlib import Path
import json
import pytest


def test_filter_duplicates_against_qwen_vocab():
    """Tokens already in Qwen's vocab must be dropped."""
    from extend_qwen_tokenizer import filter_new_tokens
    qwen_vocab = {"hello": 100, "(defun": 200, "Ġworld": 300}
    candidates = ["hello", "str_substring", "(defun", "math_floor"]
    new_only = filter_new_tokens(candidates, qwen_vocab)
    assert new_only == ["str_substring", "math_floor"]


def test_assigns_ids_starting_at_qwen_total_size():
    from extend_qwen_tokenizer import assign_new_ids
    qwen_total = 151665
    candidates = ["str_substring", "math_floor", "list_length"]
    id_map = assign_new_ids(candidates, qwen_total)
    assert id_map == {
        "str_substring": 151665,
        "math_floor":    151666,
        "list_length":   151667,
    }


def test_assign_new_ids_preserves_order():
    """Order matters — same candidate list must produce same ID map every run."""
    from extend_qwen_tokenizer import assign_new_ids
    a = assign_new_ids(["x", "y", "z"], 100)
    b = assign_new_ids(["x", "y", "z"], 100)
    assert a == b


def test_filter_new_tokens_preserves_order():
    from extend_qwen_tokenizer import filter_new_tokens
    qwen = {"b": 1}
    cands = ["a", "b", "c", "d"]
    assert filter_new_tokens(cands, qwen) == ["a", "c", "d"]
