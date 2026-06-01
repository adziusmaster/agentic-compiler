"""Tests for build_agc_corpus.py: extracting AGC source into one corpus file."""
from pathlib import Path
import pytest
from build_agc_corpus import (
    extract_solutions_from_jsonl,
    extract_ag_files,
    build_corpus,
)

FIXTURES = Path(__file__).parent / "fixtures"


def test_extract_solutions_from_jsonl_yields_solution_strings():
    pairs_path = FIXTURES / "tiny_pairs.jsonl"
    solutions = list(extract_solutions_from_jsonl(pairs_path))
    assert len(solutions) == 3
    assert "(defun id (n)" in solutions[0]
    assert "(defun inc (n)" in solutions[1]
    assert "(defun double (n)" in solutions[2]


def test_extract_solutions_skips_records_without_solution(tmp_path):
    bad = tmp_path / "mixed.jsonl"
    bad.write_text(
        '{"objective": "x"}\n'
        '{"solution": "(module M (defun f () (return 1)))"}\n'
    )
    solutions = list(extract_solutions_from_jsonl(bad))
    assert len(solutions) == 1
    assert "(defun f" in solutions[0]


def test_extract_ag_files_yields_file_contents(tmp_path):
    (tmp_path / "a.ag").write_text("(module A (defun a () (return 0)))")
    (tmp_path / "b.ag").write_text("(module B (defun b () (return 1)))")
    (tmp_path / "ignored.txt").write_text("not an ag file")
    contents = sorted(extract_ag_files(tmp_path))
    assert len(contents) == 2
    assert any("(defun a" in c for c in contents)
    assert any("(defun b" in c for c in contents)


def test_build_corpus_writes_newline_delimited(tmp_path, monkeypatch):
    out = tmp_path / "corpus.txt"
    pairs = FIXTURES / "tiny_pairs.jsonl"
    count = build_corpus(
        out_path=out,
        jsonl_paths=[pairs],
        ag_search_root=None,
    )
    assert count == 3
    text = out.read_text()
    assert text.count("\n") == 3
    assert "(defun id" in text
