"""Tests for the secondary-tokenizer counting helper added to eval_checkpoint_cascade.py."""
from unittest.mock import MagicMock
import pytest


def test_count_with_secondary_returns_int():
    from eval_checkpoint_cascade import count_with_tokenizer
    fake_tok = MagicMock()
    fake_tok.encode.return_value = [1, 2, 3, 4]
    assert count_with_tokenizer(fake_tok, "hello") == 4


def test_count_with_secondary_skips_special_tokens():
    """Secondary count should NOT add special tokens; we want raw string length."""
    from eval_checkpoint_cascade import count_with_tokenizer
    fake_tok = MagicMock()
    fake_tok.encode.return_value = [1, 2, 3]
    count_with_tokenizer(fake_tok, "x")
    fake_tok.encode.assert_called_with("x", add_special_tokens=False)


def test_count_with_secondary_handles_empty():
    from eval_checkpoint_cascade import count_with_tokenizer
    fake_tok = MagicMock()
    fake_tok.encode.return_value = []
    assert count_with_tokenizer(fake_tok, "") == 0
