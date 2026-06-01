"""Tests for the merge-init helper in patch_model_embeddings.py."""
import numpy as np
import pytest


def test_merge_init_returns_mean_of_constituent_rows():
    """For new token 'foo' that old-tokenizer encoded as IDs [1, 3], the
    new embedding row should be mean(emb[1], emb[3])."""
    from patch_model_embeddings import merge_init_row
    emb = np.array([
        [0.0, 0.0],   # id 0
        [2.0, 4.0],   # id 1
        [9.9, 9.9],   # id 2 (unused)
        [4.0, 6.0],   # id 3
    ], dtype=np.float32)
    row = merge_init_row(constituent_ids=[1, 3], embedding_matrix=emb)
    assert row.shape == (2,)
    np.testing.assert_allclose(row, [3.0, 5.0])


def test_merge_init_single_constituent_returns_that_row():
    from patch_model_embeddings import merge_init_row
    emb = np.array([[1.0, 2.0], [3.0, 4.0]], dtype=np.float32)
    row = merge_init_row([1], emb)
    np.testing.assert_allclose(row, [3.0, 4.0])


def test_merge_init_three_constituents():
    from patch_model_embeddings import merge_init_row
    emb = np.array([
        [3.0, 0.0],
        [6.0, 0.0],
        [9.0, 0.0],
    ], dtype=np.float32)
    row = merge_init_row([0, 1, 2], emb)
    np.testing.assert_allclose(row, [6.0, 0.0])


def test_merge_init_raises_on_empty_constituents():
    from patch_model_embeddings import merge_init_row
    emb = np.array([[1.0]], dtype=np.float32)
    with pytest.raises(ValueError):
        merge_init_row([], emb)


def test_merge_init_preserves_dtype():
    from patch_model_embeddings import merge_init_row
    emb = np.array([[1.0, 2.0], [3.0, 4.0]], dtype=np.float16)
    row = merge_init_row([0, 1], emb)
    assert row.dtype == np.float16
    np.testing.assert_allclose(row, [2.0, 3.0])
