# AGC Custom Tokenizer (L7) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend Qwen2.5-Coder-7B's tokenizer with ~500 AGC-specific tokens, retrain LoRA v14 on the patched base, and measure tok/pass against a clean v13 (old-vocab) baseline trained on identical data and hyperparameters.

**Architecture:** Vocab *extension* (not replacement) at IDs ≥ 151,665, merge-init embeddings (mean of constituent old-token rows), zero TCB impact. The verifier never sees the tokenizer. See `docs/superpowers/specs/2026-05-08-agc-custom-tokenizer-design.md` for full design.

**Tech Stack:** Python 3, HuggingFace `tokenizers` (0.22.2 verified installed), `transformers`, `mlx`, `mlx_lm`, existing AGC training/eval scripts (`train/run_l3.py`, `train/eval_checkpoint_cascade.py`).

---

## PROJECT CONVENTIONS — read before starting

- **NO GIT COMMITS within tasks.** This project's user handles all git operations directly. Each task ends when files are written and verified on disk. Do **not** run `git add`, `git commit`, `git status`, or any git command. The standard "commit" step in the writing-plans skill is replaced by "save and verify on disk."
- **Tests live in `train/tests/`** (created in Task 1). Run individual tests with `python3 -m pytest train/tests/test_<file>.py -v`.
- **Python interpreter is `python3`**, not `python`.
- **Plan.md is gitignored.** It already reflects the L7-active sequencing — do not commit it.
- **Working directory:** `/Users/andrzej.lech/Code/private/AgenticCompiler`. All paths are relative to this.
- **Data files referenced in this plan:**
  - `train/dataset/agc_pairs_claude.jsonl` (587 pairs, exists)
  - `train/dataset/agc_pairs_naming.jsonl` (28 pairs, exists from 2026-05-07)
  - `train/dataset/agc_pairs_v13_dual.jsonl` (does NOT exist; built by Task 0)
  - `train/dataset/agc_corpus.txt` (does NOT exist; built by Task 9)

---

## File Structure

**New files (created by this plan):**

| Path | Responsibility |
|---|---|
| `train/tests/__init__.py` | Make `train/tests/` a package. |
| `train/tests/test_build_agc_corpus.py` | Tests for C1. |
| `train/tests/test_train_agc_bpe.py` | Tests for C2 whitelist. |
| `train/tests/test_extend_qwen_tokenizer.py` | Tests for C3 dedup + ID assignment. |
| `train/tests/test_patch_model_embeddings.py` | Tests for C4 merge-init math. |
| `train/tests/test_eval_secondary_tokens.py` | Tests for the secondary-tokenizer count helper. |
| `train/tests/fixtures/` | Tiny corpora and tokenizers for unit tests. |
| `train/build_agc_corpus.py` | C1: extract AGC source → `agc_corpus.txt`. |
| `train/train_agc_bpe.py` | C2: train BPE on AGC corpus, output candidate merges. |
| `train/extend_qwen_tokenizer.py` | C3: dedup vs Qwen vocab, assign new IDs ≥ 151,665. |
| `train/patch_model_embeddings.py` | C4: load fp16 base, merge-init new rows, re-quantize. |
| `train/agc_tokenizer/` | Output: extended `tokenizer.json` + companion files. |
| `train/agc_stdlib_tokens.py` | Curated stdlib token list for whitelist filter. |
| `train/lora_adapter_v13_baseline/` | Output: v13 retrain on identical data, old vocab. |
| `train/lora_adapter_v14_tokenizer/` | Output: v14 with new vocab. |
| `~/.cache/agc/qwen-7b-extended-v1/` | Output: patched 4-bit base + colocated extended tokenizer. |
| `bench/results/2026-05-08/agc-v13-baseline-cascade.jsonl` | v13 results. |
| `bench/results/2026-05-08/agc-v14-tokenizer-cascade.jsonl` | v14 results (with secondary tok counts). |

**Modified files:**

| Path | Change |
|---|---|
| `train/run_l3.py` | Add `--train-new-embeddings` flag (unfreezes embedding rows ≥ 151,665) and `--tokenizer` thread-through. |
| `train/eval_checkpoint_cascade.py` | Add secondary-tokenizer pass: re-encode prompts+outputs with original Qwen tokenizer, write `tok_in_secondary` and `tok_out_secondary` fields per problem. |

**Untouched (load-bearing):** All of `Agentic.*` (TCB stays at ~1153 LOC). `bench/run.py`, `bench/problems/*`. `plan.md` already reflects the L7-active pivot.

---

## Task 0: Build v13/v14 training corpus (prerequisite)

The v13/v14 A/B requires the same training data the 2026-05-08 RESUME-POINT in `plan.md` describes. Build it before anything else; the file is referenced by Tasks 13 and 14.

**Files:**
- Create: `train/dataset/agc_pairs_v13_source.jsonl` (615 lines: 587 + 28)
- Create: `train/dataset/agc_pairs_v13_dual.jsonl` (1,230 lines after no-tests augmentation)

- [ ] **Step 1: Concatenate the two source corpora.**

```bash
cat train/dataset/agc_pairs_claude.jsonl \
    train/dataset/agc_pairs_naming.jsonl \
    > train/dataset/agc_pairs_v13_source.jsonl
```

- [ ] **Step 2: Verify the concatenation.**

```bash
wc -l train/dataset/agc_pairs_v13_source.jsonl
```
Expected: `615 train/dataset/agc_pairs_v13_source.jsonl`. If not, stop — one of the inputs is unexpectedly sized.

- [ ] **Step 3: Augment with no-tests variants using the existing script.**

```bash
python3 train/augment_no_tests.py \
  --in  train/dataset/agc_pairs_v13_source.jsonl \
  --out train/dataset/agc_pairs_v13_dual.jsonl
```

- [ ] **Step 4: Verify the augmentation.**

```bash
wc -l train/dataset/agc_pairs_v13_dual.jsonl
```
Expected: `1230 train/dataset/agc_pairs_v13_dual.jsonl`.
Spot-check the first two records have `format=with_tests` and `format=no_tests`:
```bash
python3 -c "import json; lines = open('train/dataset/agc_pairs_v13_dual.jsonl').readlines(); print(json.loads(lines[0])['format'], json.loads(lines[1])['format'])"
```
Expected: `with_tests no_tests`.

- [ ] **Step 5: Save and verify on disk** (no git).

```bash
ls -la train/dataset/agc_pairs_v13_*.jsonl
```
Both files present, sizes ~22 KB and ~46 KB respectively.

---

## Task 1: Set up `train/tests/` infrastructure

**Files:**
- Create: `train/tests/__init__.py`
- Create: `train/tests/conftest.py`
- Create: `train/tests/fixtures/tiny_pairs.jsonl`
- Create: `train/tests/fixtures/tiny_module.ag`

- [ ] **Step 1: Create the test package directory.**

```bash
mkdir -p train/tests/fixtures
```

- [ ] **Step 2: Create `train/tests/__init__.py` (empty file).**

```python
```

- [ ] **Step 3: Create `train/tests/conftest.py` so pytest can resolve imports from `train/`.**

```python
"""pytest config: ensure `train/` modules are importable from tests."""
import sys
from pathlib import Path

TRAIN_DIR = Path(__file__).resolve().parent.parent
if str(TRAIN_DIR) not in sys.path:
    sys.path.insert(0, str(TRAIN_DIR))
```

- [ ] **Step 4: Create `train/tests/fixtures/tiny_pairs.jsonl` with 3 minimal training pairs.**

```jsonl
{"category": "pure", "topic": "id", "objective": "Return n.", "solution": "(module Id (defun id (n) (return n)))", "tests_passed": 1, "source": "test"}
{"category": "pure", "topic": "inc", "objective": "Return n+1.", "solution": "(module Inc (defun inc (n) (return (+ n 1))))", "tests_passed": 1, "source": "test"}
{"category": "pure", "topic": "double", "objective": "Return 2n.", "solution": "(module Double (defun double (n) (return (* n 2))))", "tests_passed": 1, "source": "test"}
```

- [ ] **Step 5: Create `train/tests/fixtures/tiny_module.ag` (a representative AGC module).**

```
(module Sample
  (defun add (a b) (return (+ a b)))
  (test t1 (eq? (add 2 3) 5)))
```

- [ ] **Step 6: Verify pytest can find and run the empty test set.**

```bash
python3 -m pytest train/tests/ -v
```
Expected: `no tests ran` exit 5 (pytest exit code for no tests collected). Acceptable; we just want the import path working.

- [ ] **Step 7: Save and verify on disk.**

```bash
ls -la train/tests/
```
All four files present.

---

## Task 2: C1 corpus extractor — TDD

**Files:**
- Create: `train/build_agc_corpus.py`
- Create: `train/tests/test_build_agc_corpus.py`

- [ ] **Step 1: Write the failing tests in `train/tests/test_build_agc_corpus.py`.**

```python
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
```

- [ ] **Step 2: Run the tests and verify they fail with import error.**

```bash
python3 -m pytest train/tests/test_build_agc_corpus.py -v
```
Expected: `ImportError: cannot import name 'extract_solutions_from_jsonl' from 'build_agc_corpus'` or `ModuleNotFoundError`. That's the failing state we want before implementation.

- [ ] **Step 3: Implement `train/build_agc_corpus.py`.**

```python
#!/usr/bin/env python3
"""Extract AGC source into a single newline-delimited corpus file (C1).

Reads `solution` fields from training-pair JSONL files and `.ag` files
from the bench tree, writes them to `train/dataset/agc_corpus.txt`,
one module per line (newlines inside modules collapsed to spaces so
each line is a self-contained module).

Usage:
  python3 train/build_agc_corpus.py [--out PATH] [--jsonl PATH ...] [--ag-root PATH]
"""
from __future__ import annotations
import argparse
import json
from pathlib import Path
from typing import Iterable, Iterator

TRAIN_DIR = Path(__file__).resolve().parent
REPO_ROOT = TRAIN_DIR.parent
DEFAULT_OUT = TRAIN_DIR / "dataset" / "agc_corpus.txt"
DEFAULT_AG_ROOT = REPO_ROOT / "bench"


def extract_solutions_from_jsonl(path: Path) -> Iterator[str]:
    """Yield the `solution` field from each line of a training-pair JSONL."""
    with path.open() as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            rec = json.loads(line)
            sol = rec.get("solution")
            if sol:
                yield sol


def extract_ag_files(root: Path) -> Iterator[str]:
    """Yield the contents of every `.ag` file under `root` (recursive)."""
    for p in sorted(root.rglob("*.ag")):
        yield p.read_text()


def _flatten(module: str) -> str:
    """Collapse internal newlines so each module is one line in the corpus."""
    return " ".join(module.split())


def build_corpus(
    out_path: Path,
    jsonl_paths: Iterable[Path],
    ag_search_root: Path | None,
) -> int:
    """Write all AGC source to `out_path`, one module per line. Returns count."""
    count = 0
    out_path.parent.mkdir(parents=True, exist_ok=True)
    with out_path.open("w") as out:
        for jp in jsonl_paths:
            for sol in extract_solutions_from_jsonl(jp):
                out.write(_flatten(sol) + "\n")
                count += 1
        if ag_search_root is not None:
            for content in extract_ag_files(ag_search_root):
                out.write(_flatten(content) + "\n")
                count += 1
    return count


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT)
    ap.add_argument(
        "--jsonl",
        type=Path,
        action="append",
        default=None,
        help="Training-pair JSONL files. May be repeated. "
             "Default: all train/dataset/agc_pairs_*.jsonl",
    )
    ap.add_argument("--ag-root", type=Path, default=DEFAULT_AG_ROOT)
    args = ap.parse_args()

    if args.jsonl is None:
        args.jsonl = sorted((TRAIN_DIR / "dataset").glob("agc_pairs_*.jsonl"))

    n = build_corpus(args.out, args.jsonl, args.ag_root)
    print(f"Wrote {n} modules to {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
```

- [ ] **Step 4: Run the tests to verify they pass.**

```bash
python3 -m pytest train/tests/test_build_agc_corpus.py -v
```
Expected: 4 passed.

- [ ] **Step 5: Save and verify on disk.**

```bash
ls -la train/build_agc_corpus.py train/tests/test_build_agc_corpus.py
```

---

## Task 3: C2 stdlib token list + whitelist filter — TDD

**Files:**
- Create: `train/agc_stdlib_tokens.py`
- Create: `train/tests/test_train_agc_bpe.py`

The whitelist filter rejects user-defined identifiers from the corpus. It needs a curated list of "valid AGC stdlib + keywords" that are kept regardless.

- [ ] **Step 1: Write the failing tests.**

```python
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
```

- [ ] **Step 2: Run the tests and verify they fail.**

```bash
python3 -m pytest train/tests/test_train_agc_bpe.py -v
```
Expected: `ImportError` — module doesn't exist yet.

- [ ] **Step 3: Implement `train/agc_stdlib_tokens.py` (the curated stdlib list).**

```python
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
```

- [ ] **Step 4: Implement `train/train_agc_bpe.py` with `is_valid_agc_token` + minimal CLI.**

```python
#!/usr/bin/env python3
"""Train a BPE on AGC-only corpus, output candidate tokens (C2).

Uses HuggingFace `tokenizers` with the same byte-level pre-tokenizer
config Qwen uses, so resulting merges are byte-compatible.

A whitelist filter keeps only candidates that are real AGC syntax
(stdlib calls, keywords, type annotations, operators) and rejects
user-defined identifiers from the corpus.

Usage:
  python3 train/train_agc_bpe.py \
    --in  train/dataset/agc_corpus.txt \
    --out train/agc_tokenizer/ \
    --vocab-size 500
"""
from __future__ import annotations
import argparse
import json
import re
from pathlib import Path

from agc_stdlib_tokens import AGC_STDLIB

TRAIN_DIR = Path(__file__).resolve().parent
DEFAULT_IN = TRAIN_DIR / "dataset" / "agc_corpus.txt"
DEFAULT_OUT_DIR = TRAIN_DIR / "agc_tokenizer"

# Tokens with these prefixes are s-expr openers — keep regardless of length.
SEXPR_PREFIXES = ("(", ")")
# Type-annotation prefixes
TYPE_PREFIXES = (": ", "-> ")


def is_valid_agc_token(token: str, stdlib: set[str]) -> bool:
    """Return True if `token` should be kept as an AGC vocab extension."""
    if not token:
        return False
    # Strip any leading byte-level marker (Qwen uses 'Ġ' for leading space)
    bare = token.lstrip("Ġ").lstrip()
    if not bare:
        return False
    # Keep s-expr openers like "(defun", "(assert-eq", "(extern"
    if bare.startswith("("):
        inner = bare[1:]
        if inner in stdlib:
            return True
        # Short opener like "(do" is OK
        if len(inner) <= 4 and re.match(r"^[a-zA-Z\-?!*+]+$", inner):
            return True
        return False
    # Keep type annotations
    for pref in TYPE_PREFIXES:
        if bare.startswith(pref):
            ann = bare[len(pref):]
            if ann in stdlib:
                return True
    # Keep stdlib symbols verbatim
    if bare in stdlib:
        return True
    # Keep short tokens (≤4 chars) that look like operators / keywords
    if len(bare) <= 4 and re.match(r"^[a-zA-Z\-?!*+=<>]+$", bare):
        return True
    # Reject everything else (user identifiers, random fragments)
    return False


def train_bpe(corpus_path: Path, vocab_size: int, out_dir: Path) -> Path:
    """Train BPE on `corpus_path`, save raw tokenizer.json to out_dir.

    Returns the path to the saved raw tokenizer.json (filtering happens
    in extend_qwen_tokenizer.py).
    """
    from tokenizers import Tokenizer, models, trainers, pre_tokenizers

    out_dir.mkdir(parents=True, exist_ok=True)
    tok = Tokenizer(models.BPE(unk_token="<unk>"))
    tok.pre_tokenizer = pre_tokenizers.ByteLevel(add_prefix_space=False)
    trainer = trainers.BpeTrainer(
        vocab_size=vocab_size,
        special_tokens=["<unk>"],
        initial_alphabet=pre_tokenizers.ByteLevel.alphabet(),
        show_progress=True,
    )
    tok.train(files=[str(corpus_path)], trainer=trainer)
    raw_path = out_dir / "raw_bpe.json"
    tok.save(str(raw_path))
    return raw_path


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--in", dest="corpus_in", type=Path, default=DEFAULT_IN)
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT_DIR)
    ap.add_argument("--vocab-size", type=int, default=500)
    args = ap.parse_args()

    raw_path = train_bpe(args.corpus_in, args.vocab_size, args.out)

    # Print top-50 candidates after applying the whitelist filter (sanity preview).
    with raw_path.open() as f:
        raw = json.load(f)
    vocab = raw["model"]["vocab"]
    sorted_tokens = sorted(vocab.items(), key=lambda kv: kv[1])
    kept = [t for t, _ in sorted_tokens if is_valid_agc_token(t, AGC_STDLIB)]
    print(f"Trained BPE with vocab_size={args.vocab_size}; "
          f"kept {len(kept)}/{len(vocab)} after whitelist filter.")
    print("Top 50 kept candidates:")
    for t in kept[:50]:
        print(f"  {t!r}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
```

- [ ] **Step 5: Run the whitelist tests.**

```bash
python3 -m pytest train/tests/test_train_agc_bpe.py -v
```
Expected: 6 passed.

- [ ] **Step 6: Save and verify on disk.**

```bash
ls -la train/agc_stdlib_tokens.py train/train_agc_bpe.py
```

---

## Task 4: C3 vocab extender — TDD

**Files:**
- Create: `train/extend_qwen_tokenizer.py`
- Create: `train/tests/test_extend_qwen_tokenizer.py`

- [ ] **Step 1: Write the failing tests.**

```python
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
```

- [ ] **Step 2: Run the tests and verify they fail.**

```bash
python3 -m pytest train/tests/test_extend_qwen_tokenizer.py -v
```
Expected: `ImportError`.

- [ ] **Step 3: Implement `train/extend_qwen_tokenizer.py`.**

```python
#!/usr/bin/env python3
"""Extend Qwen's tokenizer with AGC-specific tokens (C3).

Reads candidates from train_agc_bpe.py's raw output, applies the
whitelist filter, deduplicates against Qwen's existing vocab, assigns
new IDs starting at Qwen's total vocab size, writes a HF-compatible
tokenizer.json that AutoTokenizer.from_pretrained can load.

Decision gate: if fewer than 200 net-new tokens survive, prints a
warning and exits non-zero so a human can decide whether to proceed.

Usage:
  python3 train/extend_qwen_tokenizer.py \
    --raw   train/agc_tokenizer/raw_bpe.json \
    --qwen  mlx-community/Qwen2.5-Coder-7B-Instruct-4bit \
    --out   train/agc_tokenizer/ \
    --min-new 200
"""
from __future__ import annotations
import argparse
import json
import sys
from pathlib import Path
from typing import Sequence

from agc_stdlib_tokens import AGC_STDLIB
from train_agc_bpe import is_valid_agc_token

TRAIN_DIR = Path(__file__).resolve().parent
DEFAULT_RAW = TRAIN_DIR / "agc_tokenizer" / "raw_bpe.json"
DEFAULT_OUT_DIR = TRAIN_DIR / "agc_tokenizer"
DEFAULT_QWEN = "mlx-community/Qwen2.5-Coder-7B-Instruct-4bit"


def filter_new_tokens(candidates: Sequence[str], qwen_vocab: dict[str, int]) -> list[str]:
    """Drop any candidate already present in `qwen_vocab`. Preserve order."""
    return [c for c in candidates if c not in qwen_vocab]


def assign_new_ids(candidates: Sequence[str], start_id: int) -> dict[str, int]:
    """Assign sequential IDs starting at `start_id`. Order-preserving."""
    return {c: start_id + i for i, c in enumerate(candidates)}


def _load_qwen_vocab(qwen_repo_or_path: str) -> tuple[dict[str, int], dict]:
    """Return (vocab, full_tokenizer_json_obj) from a Qwen tokenizer."""
    from transformers import AutoTokenizer
    tok = AutoTokenizer.from_pretrained(qwen_repo_or_path)
    vocab = tok.get_vocab()
    # Round-trip through tokenizer.json so we can edit added_tokens.
    tok_json_path = Path(tok.vocab_files_names.get("tokenizer_file", "tokenizer.json"))
    # Most reliable: re-serialize via save_pretrained to a tmp dir and read back.
    return vocab, {}  # full json is loaded separately in main(); this helper used in tests only.


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--raw", type=Path, default=DEFAULT_RAW)
    ap.add_argument("--qwen", default=DEFAULT_QWEN)
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT_DIR)
    ap.add_argument("--min-new", type=int, default=200,
                    help="Decision gate: minimum acceptable net-new token count.")
    args = ap.parse_args()

    # 1. Load Qwen tokenizer's full vocab + tokenizer.json
    from transformers import AutoTokenizer
    qwen_tok = AutoTokenizer.from_pretrained(args.qwen)
    qwen_vocab = qwen_tok.get_vocab()
    qwen_total = max(qwen_vocab.values()) + 1
    print(f"Qwen vocab size (incl special): {qwen_total}")

    # 2. Load raw BPE candidates
    raw = json.loads(args.raw.read_text())
    raw_vocab = raw["model"]["vocab"]
    candidates_in_order = [t for t, _ in sorted(raw_vocab.items(), key=lambda kv: kv[1])]

    # 3. Apply whitelist filter
    after_whitelist = [t for t in candidates_in_order if is_valid_agc_token(t, AGC_STDLIB)]
    print(f"After whitelist: {len(after_whitelist)} / {len(candidates_in_order)}")

    # 4. Deduplicate against Qwen
    new_only = filter_new_tokens(after_whitelist, qwen_vocab)
    print(f"After dedup vs Qwen: {len(new_only)} (dropped "
          f"{len(after_whitelist) - len(new_only)} already in Qwen)")

    # 5. Decision gate
    if len(new_only) < args.min_new:
        print(f"WARNING: only {len(new_only)} new tokens, below threshold {args.min_new}.")
        print("Pause and adjust BPE settings before proceeding.")
        return 1

    # 6. Assign new IDs
    id_map = assign_new_ids(new_only, qwen_total)

    # 7. Save extended tokenizer (use add_tokens API, save_pretrained)
    qwen_tok.add_tokens(new_only)
    args.out.mkdir(parents=True, exist_ok=True)
    qwen_tok.save_pretrained(str(args.out))

    # 8. Save id_map as a sidecar for downstream embedding patching
    (args.out / "new_token_id_map.json").write_text(
        json.dumps(id_map, indent=2, ensure_ascii=False)
    )
    print(f"Wrote extended tokenizer to {args.out}")
    print(f"New token count: {len(new_only)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
```

- [ ] **Step 4: Run the tests to verify they pass.**

```bash
python3 -m pytest train/tests/test_extend_qwen_tokenizer.py -v
```
Expected: 4 passed.

- [ ] **Step 5: Save and verify on disk.**

```bash
ls -la train/extend_qwen_tokenizer.py
```

---

## Task 5: C4 merge-init function — TDD (pure logic)

**Files:**
- Create: `train/patch_model_embeddings.py` (initial: just the merge-init helper)
- Create: `train/tests/test_patch_model_embeddings.py`

The merge-init math is testable independently of the 14 GB model load. Test it first; layer the model-loading code on top in Task 6.

- [ ] **Step 1: Write the failing tests.**

```python
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
```

- [ ] **Step 2: Run the tests and verify they fail.**

```bash
python3 -m pytest train/tests/test_patch_model_embeddings.py -v
```
Expected: `ImportError`.

- [ ] **Step 3: Implement the merge-init helper (just this for now; full patcher in Task 6).**

```python
#!/usr/bin/env python3
"""Patch Qwen embeddings for new AGC tokens (C4).

Loads fp16 base, decomposes each new token via the old tokenizer to get
its constituent IDs, sets the new embedding row to mean(constituent rows),
re-quantizes to 4-bit, saves to ~/.cache/agc/qwen-7b-extended-v1/.

This file initially contains only the pure merge-init helper (tested
independently). The full model-loading path is appended in Task 6.

Usage (Task 6 onward):
  python3 train/patch_model_embeddings.py \
    --base-model Qwen/Qwen2.5-Coder-7B-Instruct \
    --tokenizer  train/agc_tokenizer \
    --out        ~/.cache/agc/qwen-7b-extended-v1
"""
from __future__ import annotations
from typing import Sequence
import numpy as np


def merge_init_row(
    constituent_ids: Sequence[int],
    embedding_matrix: np.ndarray,
) -> np.ndarray:
    """Return the merge-init embedding row for a new token.

    The new row is the mean of the rows in `embedding_matrix` indexed by
    `constituent_ids`. Preserves the matrix's dtype.

    Raises ValueError on empty constituent_ids.
    """
    if len(constituent_ids) == 0:
        raise ValueError("constituent_ids must not be empty")
    rows = embedding_matrix[list(constituent_ids)]
    # mean() upcasts fp16 -> fp32; cast back to preserve dtype.
    mean = rows.mean(axis=0)
    return mean.astype(embedding_matrix.dtype)
```

- [ ] **Step 4: Run the tests to verify they pass.**

```bash
python3 -m pytest train/tests/test_patch_model_embeddings.py -v
```
Expected: 5 passed.

- [ ] **Step 5: Save and verify on disk.**

```bash
ls -la train/patch_model_embeddings.py
```

---

## Task 6: C4 full model patcher — implementation

This task wires `merge_init_row` into a full pipeline: load fp16 7B → patch embeddings → re-quantize to 4-bit → save with colocated tokenizer.

**Files:**
- Modify: `train/patch_model_embeddings.py` (append main pipeline)

- [ ] **Step 1: Append the full pipeline to `train/patch_model_embeddings.py`** (keep existing `merge_init_row` at the top of the file).

```python


# ----- model-loading pipeline (added in Task 6) -----

import argparse
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

DEFAULT_BASE = "Qwen/Qwen2.5-Coder-7B-Instruct"
DEFAULT_OUT = Path.home() / ".cache" / "agc" / "qwen-7b-extended-v1"


def _decompose_with_old_tokenizer(token: str, old_tok) -> list[int]:
    """Tokenize `token` with the old tokenizer; return constituent IDs."""
    ids = old_tok.encode(token, add_special_tokens=False)
    if not ids:
        raise ValueError(f"token {token!r} produced no constituent IDs")
    return ids


def patch_and_save(
    base_model_id: str,
    tokenizer_dir: Path,
    out_dir: Path,
) -> None:
    """Full C4 pipeline. Side effect: writes patched model + tokenizer to out_dir."""
    from transformers import AutoTokenizer, AutoModelForCausalLM
    import torch

    out_dir.mkdir(parents=True, exist_ok=True)

    # 1. Load fp16 base
    print(f"Loading {base_model_id} in fp16...")
    base = AutoModelForCausalLM.from_pretrained(
        base_model_id, torch_dtype=torch.float16
    )
    old_tok = AutoTokenizer.from_pretrained(base_model_id)

    # 2. Load extended tokenizer + new-token map
    new_tok = AutoTokenizer.from_pretrained(str(tokenizer_dir))
    id_map = json.loads((tokenizer_dir / "new_token_id_map.json").read_text())
    print(f"Extending vocab by {len(id_map)} new tokens.")

    # 3. Resize model embeddings (creates new rows, randomly init by default)
    old_size = base.get_input_embeddings().weight.shape[0]
    new_size = old_size + len(id_map)
    base.resize_token_embeddings(new_size)
    embed = base.get_input_embeddings()
    output_embed = base.get_output_embeddings()
    is_tied = base.config.tie_word_embeddings
    print(f"Resized embeddings: {old_size} -> {new_size}; "
          f"tie_word_embeddings={is_tied}")

    # 4. Merge-init each new row
    embed_np = embed.weight.detach().cpu().numpy()
    out_np = None
    if not is_tied and output_embed is not None:
        out_np = output_embed.weight.detach().cpu().numpy()

    for tok, new_id in id_map.items():
        constituent = _decompose_with_old_tokenizer(tok, old_tok)
        new_row = merge_init_row(constituent, embed_np[:old_size])
        embed_np[new_id] = new_row
        if out_np is not None:
            new_out_row = merge_init_row(constituent, out_np[:old_size])
            out_np[new_id] = new_out_row

    # Write back into the model
    embed.weight.data.copy_(torch.from_numpy(embed_np))
    if out_np is not None:
        output_embed.weight.data.copy_(torch.from_numpy(out_np))

    # 5. Save fp16 patched model + tokenizer to a staging dir
    staging = out_dir.parent / (out_dir.name + "-fp16-staging")
    staging.mkdir(parents=True, exist_ok=True)
    base.save_pretrained(staging)
    new_tok.save_pretrained(staging)
    print(f"fp16 patched model staged at {staging}")

    # 6. Re-quantize to 4-bit using mlx-lm convert
    print("Re-quantizing to 4-bit via mlx_lm.convert...")
    subprocess.run(
        [
            sys.executable, "-m", "mlx_lm", "convert",
            "--hf-path", str(staging),
            "--mlx-path", str(out_dir),
            "-q", "--q-bits", "4",
        ],
        check=True,
    )

    # 7. Copy the extended tokenizer files into out_dir (in case mlx_lm convert dropped them)
    for fname in ("tokenizer.json", "tokenizer_config.json", "special_tokens_map.json", "new_token_id_map.json"):
        src = tokenizer_dir / fname
        if src.exists():
            shutil.copy2(src, out_dir / fname)

    print(f"Done. Patched 4-bit model at {out_dir}")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--base-model", default=DEFAULT_BASE)
    ap.add_argument(
        "--tokenizer", type=Path,
        default=Path(__file__).resolve().parent / "agc_tokenizer",
    )
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT)
    args = ap.parse_args()

    patch_and_save(args.base_model, args.tokenizer, args.out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
```

- [ ] **Step 2: Verify the file still passes the merge-init unit tests** (the appended code shouldn't break them).

```bash
python3 -m pytest train/tests/test_patch_model_embeddings.py -v
```
Expected: 5 passed (same as before — the unit-tested helper is untouched).

- [ ] **Step 3: Verify the new code at least parses (no syntax errors).**

```bash
python3 -c "import patch_model_embeddings" 2>&1 | head -5
```
Run from inside `train/`:
```bash
cd train && python3 -c "import patch_model_embeddings; print('OK')" && cd ..
```
Expected: `OK`.

- [ ] **Step 4: Save and verify on disk.**

```bash
wc -l train/patch_model_embeddings.py
```
Expected: ~150 lines.

---

## Task 7: Modify `eval_checkpoint_cascade.py` for secondary token counts — TDD

**Files:**
- Create: `train/tests/test_eval_secondary_tokens.py`
- Modify: `train/eval_checkpoint_cascade.py`

We need a helper that, given a generated string and a "secondary tokenizer" (original Qwen), returns the token count under that tokenizer. Then the cascade harness records both counts per problem.

- [ ] **Step 1: Read the current `eval_checkpoint_cascade.py` to find the right hook points.**

Read `train/eval_checkpoint_cascade.py` start to end. Specifically locate:
- The `try_problem` function (around line 70) — where `prompt_toks` and `gen_toks` are computed.
- The `main()` flow (around line 158) — where the result dict is assembled per problem.

- [ ] **Step 2: Write the failing tests.**

```python
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
```

- [ ] **Step 3: Run the tests and verify they fail.**

```bash
python3 -m pytest train/tests/test_eval_secondary_tokens.py -v
```
Expected: `ImportError: cannot import name 'count_with_tokenizer'`.

- [ ] **Step 4: Add `count_with_tokenizer` to `train/eval_checkpoint_cascade.py`.**

Locate the imports section near the top of the file and add the helper just before `try_problem`. Insert this exact function (do not modify any other code yet):

```python
def count_with_tokenizer(tok, text: str) -> int:
    """Count tokens in `text` under an arbitrary tokenizer (no special tokens).

    Used to record a secondary token count under the original Qwen tokenizer
    when the active tokenizer is the AGC-extended one. Lets us report both
    Y (active-vocab tok/pass) and Z (re-encoded with original Qwen) per result.
    """
    return len(tok.encode(text, add_special_tokens=False))
```

- [ ] **Step 5: Run the tests to verify they pass.**

```bash
python3 -m pytest train/tests/test_eval_secondary_tokens.py -v
```
Expected: 3 passed.

- [ ] **Step 6: Wire the secondary count into `try_problem` and the result dict.**

In `train/eval_checkpoint_cascade.py`:

(a) Add a CLI flag in `main()` (search for `argparse` / `add_argument` calls):

```python
ap.add_argument(
    "--secondary-tokenizer",
    default=None,
    help="HF tokenizer repo or path. If set, eval reports tok counts "
         "under this tokenizer alongside the primary counts.",
)
```

(b) In `main()`, after loading the primary tokenizer (around the `model, tokenizer = load(...)` line), load the secondary if requested. Insert immediately after that line:

```python
secondary_tok = None
if args.secondary_tokenizer:
    from transformers import AutoTokenizer
    secondary_tok = AutoTokenizer.from_pretrained(args.secondary_tokenizer)
```

(c) Modify `try_problem`'s signature to accept the secondary tokenizer, and compute secondary counts. Find the line around `prompt_toks = len(tokenizer.encode(chat_prompt))` and `gen_toks += len(tokenizer.encode(text))`. Add right after each:

```python
# Original primary count (unchanged):
prompt_toks = len(tokenizer.encode(chat_prompt))
prompt_toks_secondary = (
    count_with_tokenizer(secondary_tok, chat_prompt) if secondary_tok else None
)
```

And after the gen-toks tally:

```python
gen_toks += len(tokenizer.encode(text))
if secondary_tok is not None:
    gen_toks_secondary += count_with_tokenizer(secondary_tok, text)
```

Initialize `gen_toks_secondary = 0` alongside the existing `gen_toks = 0`.

(d) In the result dict (search for `"tokens_in"`), add the secondary fields:

```python
result = {
    # ... existing fields ...
    "tokens_in": prompt_toks,
    "tokens_out": gen_toks,
    "tokens_in_secondary": prompt_toks_secondary,    # NEW
    "tokens_out_secondary": gen_toks_secondary if secondary_tok is not None else None,  # NEW
    # ... rest unchanged ...
}
```

(e) Pass `secondary_tok` through `try_problem(..., secondary_tok=secondary_tok)` at the call site in `main()`.

- [ ] **Step 7: Verify the modified script still imports cleanly.**

```bash
cd train && python3 -c "import eval_checkpoint_cascade; print('OK')" && cd ..
```
Expected: `OK`.

- [ ] **Step 8: Verify the help output shows the new flag.**

```bash
python3 train/eval_checkpoint_cascade.py --help 2>&1 | grep secondary
```
Expected: `--secondary-tokenizer ...` line present.

- [ ] **Step 9: Save and verify on disk.**

```bash
wc -l train/eval_checkpoint_cascade.py
```
(Should be ~20-30 lines longer than before.)

---

## Task 8: Modify `run_l3.py` for `--train-new-embeddings` flag

**Files:**
- Modify: `train/run_l3.py`

The mlx-lm LoRA training only updates `q_proj`/`v_proj` by default. For v14, the new embedding rows must also be trainable so the model learns to use the new tokens.

- [ ] **Step 1: Read the current `train/run_l3.py` to locate where the YAML config is generated.**

Look for `subprocess.run([... "lora" ...])` or YAML writes. Identify how lora layers are configured.

- [ ] **Step 2: Add the flag.**

In the `argparse` block of `main()`, alongside the existing `--lora-rank` / `--lr-max` flags, add:

```python
ap.add_argument(
    "--train-new-embeddings",
    action="store_true",
    help="Mark embedding rows >= 151665 as trainable. "
         "Required when training on a vocab-extended base.",
)
ap.add_argument(
    "--tokenizer",
    type=Path,
    default=None,
    help="Optional path to an alternate tokenizer dir. "
         "Defaults to the tokenizer colocated with --model.",
)
```

- [ ] **Step 3: Wire the flag into the mlx-lm config.**

mlx-lm accepts a YAML config that includes a `lora_parameters.keys` list (the layer name patterns to LoRA-fine-tune). To also train new embedding rows, add the embedding layer to that list when the flag is set.

Find the dict / YAML construction in `run_l3.py` (likely a Python dict written via `yaml.safe_dump`). Locate the `lora_parameters` or `lora_layers` config block. Add this conditional block right where the LoRA target keys are defined:

```python
lora_keys = ["q_proj", "v_proj"]   # default mlx-lm LoRA targets
if args.train_new_embeddings:
    # Unfreeze the embedding layer entirely; the new rows ride along.
    # mlx-lm's full-fine-tune mode can be triggered per-layer; the
    # cleanest way is to add the embedding key to lora_parameters.keys.
    lora_keys.append("embed_tokens")

config_dict["lora_parameters"]["keys"] = lora_keys
```

If `lora_parameters.keys` doesn't already exist in the dict, create the structure:

```python
config_dict.setdefault("lora_parameters", {})
config_dict["lora_parameters"]["keys"] = lora_keys
```

- [ ] **Step 4: Verify the script still imports cleanly.**

```bash
cd train && python3 -c "import run_l3; print('OK')" && cd ..
```
Expected: `OK`.

- [ ] **Step 5: Verify the help output shows the new flags.**

```bash
python3 train/run_l3.py --help 2>&1 | grep -E "train-new-embeddings|tokenizer"
```
Expected: both flags listed.

- [ ] **Step 6: Save and verify on disk.**

```bash
wc -l train/run_l3.py
```

---

## Task 9: P1 — Run corpus build (operational)

**Files:**
- Output: `train/dataset/agc_corpus.txt`

- [ ] **Step 1: Run the corpus extractor on the default sources.**

```bash
python3 train/build_agc_corpus.py
```

- [ ] **Step 2: Verify output.**

```bash
ls -la train/dataset/agc_corpus.txt
wc -l train/dataset/agc_corpus.txt
head -3 train/dataset/agc_corpus.txt
```
Expected:
- File ~2-4 MB.
- Line count ~14,500-15,000 (14,484 modules from JSONL + ~84 .ag files = ~14,568).
- First 3 lines look like valid AGC s-expressions on single lines.

- [ ] **Step 3: Sanity-check the last line too.**

```bash
tail -1 train/dataset/agc_corpus.txt
```
Expected: a complete AGC module on one line, ending with `)`.

---

## Task 10: P2 — Run BPE training (operational)

**Files:**
- Output: `train/agc_tokenizer/raw_bpe.json`

- [ ] **Step 1: Train the BPE.**

```bash
python3 train/train_agc_bpe.py \
  --in train/dataset/agc_corpus.txt \
  --out train/agc_tokenizer \
  --vocab-size 500
```

- [ ] **Step 2: Inspect the top-50 candidates from the script's stdout.**

The script prints `Top 50 kept candidates:` after training. Verify that:
- The list contains obvious AGC stdlib calls (e.g., `str_substring`, `math_floor`, `assert-eq`).
- It contains s-expr openers (`(defun`, `(extern`, `(test`).
- It does **not** contain random byte fragments (`xQ`, `\xff\x00`-style strings).

If the candidates look like random bytes rather than AGC syntax: the byte-level pre-tokenizer config is wrong. Stop and debug `train_agc_bpe.py` before continuing.

- [ ] **Step 3: Verify output file present.**

```bash
ls -la train/agc_tokenizer/raw_bpe.json
```

---

## Task 11: P3 — Run vocab extension with decision gate

**Files:**
- Output: `train/agc_tokenizer/tokenizer.json`, `tokenizer_config.json`, `new_token_id_map.json`

- [ ] **Step 1: Run the extender with the default min-new gate (200).**

```bash
python3 train/extend_qwen_tokenizer.py \
  --raw  train/agc_tokenizer/raw_bpe.json \
  --qwen mlx-community/Qwen2.5-Coder-7B-Instruct-4bit \
  --out  train/agc_tokenizer \
  --min-new 200
```

- [ ] **Step 2: Read the script's output. Decision gate:**

- If `New token count: X` with X ≥ 200: continue.
- If X < 200: stop. The corpus may be too small or whitelist too aggressive. Adjust `vocab_size` (try 1000) in Task 10 and re-run, or relax the whitelist filter in `agc_stdlib_tokens.py`. Do not proceed to model patching with under-200 new tokens.

- [ ] **Step 3: Smoke-test the extended tokenizer encodes AGC compactly.**

```bash
python3 -c "
from transformers import AutoTokenizer
tok = AutoTokenizer.from_pretrained('train/agc_tokenizer')
samples = [
    '(defun foo (x: Num) -> Num (return (+ x 1)))',
    '(extern str_split (s: Str d: Str) -> List)',
    '(test t1 (do (def r (call foo 5)) (assert-eq r 6)))',
]
for s in samples:
    ids = tok.encode(s, add_special_tokens=False)
    print(f'{len(ids):3d} tokens : {s!r}')
"
```

Expected: lower token counts than the Qwen baseline measured earlier (16, 15, 22). If counts are unchanged, the new tokens aren't wired correctly into the tokenizer.

- [ ] **Step 4: Save and verify on disk.**

```bash
ls -la train/agc_tokenizer/
```
Expected: `tokenizer.json`, `tokenizer_config.json`, `special_tokens_map.json`, `new_token_id_map.json`, `raw_bpe.json` all present.

---

## Task 12: P4 — Patch the model embeddings

**Files:**
- Output: `~/.cache/agc/qwen-7b-extended-v1/` (4-bit MLX model dir with extended tokenizer)

This task downloads ~14 GB (fp16 7B) the first time. It will take ~5-10 min on a fast connection plus ~2 min for patching plus ~3 min for re-quantization.

- [ ] **Step 1: Run the patcher.**

```bash
python3 train/patch_model_embeddings.py \
  --base-model Qwen/Qwen2.5-Coder-7B-Instruct \
  --tokenizer  train/agc_tokenizer \
  --out        ~/.cache/agc/qwen-7b-extended-v1
```

Watch the output. Expected progression:
- "Loading Qwen/Qwen2.5-Coder-7B-Instruct in fp16..."
- "Extending vocab by N new tokens." (matches Task 11's count)
- "Resized embeddings: 152064 -> 152064+N; tie_word_embeddings=False" (or True; record which)
- "fp16 patched model staged at ..."
- "Re-quantizing to 4-bit via mlx_lm.convert..."
- "Done. Patched 4-bit model at ..."

- [ ] **Step 2: Smoke test 1 — re-quantized model still generates coherent English.**

```bash
python3 -c "
from mlx_lm import load, generate
model, tok = load('$HOME/.cache/agc/qwen-7b-extended-v1')
print(generate(model, tok, prompt='Hello, how are', max_tokens=20, verbose=False))
"
```
Expected: coherent English continuation (e.g., "you today? I'm doing well..."). If gibberish, re-quantization broke the model — fall back to mixed-precision (keep `embed_tokens` and `lm_head` in fp16). See spec R4 for fallback details.

- [ ] **Step 3: Smoke test 2 — new tokens encode and forward-pass without NaN.**

```bash
python3 -c "
from mlx_lm import load
import mlx.core as mx
model, tok = load('$HOME/.cache/agc/qwen-7b-extended-v1')
ids = tok.encode('str_substring math_floor (assert-eq', add_special_tokens=False)
print('encoded ids:', ids)
print('any >= 151665?:', any(i >= 151665 for i in ids))
# Forward pass
out = model(mx.array([ids]))
print('logits shape:', out.shape)
print('any NaN?:', bool(mx.any(mx.isnan(out)).item()))
"
```
Expected:
- `any >= 151665?: True` (confirms new tokens are being used).
- `any NaN?: False`.

If `any >= 151665?` is False, the tokenizer in the model dir doesn't include the extensions — re-copy `tokenizer.json` from `train/agc_tokenizer/` into the model dir.

- [ ] **Step 4: Smoke test 3 — tied vs untied embedding sanity.**

```bash
python3 -c "
import json
from pathlib import Path
cfg = json.loads(Path.home().joinpath('.cache/agc/qwen-7b-extended-v1/config.json').read_text())
print('tie_word_embeddings:', cfg.get('tie_word_embeddings'))
print('vocab_size:', cfg.get('vocab_size'))
"
```
Expected: `vocab_size` matches the original 152,064 + new-token-count from Task 11. `tie_word_embeddings` value is recorded for future reference.

- [ ] **Step 5: Save and verify on disk.**

```bash
ls -la ~/.cache/agc/qwen-7b-extended-v1/
du -sh ~/.cache/agc/qwen-7b-extended-v1/
```
Expected: ~4-5 GB total. Files: `config.json`, `tokenizer.json`, `model.safetensors` (or sharded), plus the new-token-id-map.

---

## Task 13: P5a — Train v13 baseline (old vocab, identical data)

**Files:**
- Output: `train/lora_adapter_v13_baseline/` (with `adapters.safetensors`, several `0000NNN_adapters.safetensors` checkpoints, `adapter_config.json`)

This is the clean A/B baseline. Uses the existing 4-bit Qwen base + the v13_dual training corpus from Task 0. No new tokenizer.

- [ ] **Step 1: Kick off v13 baseline training (~75 min wall).**

```bash
python3 train/run_l3.py \
  --model mlx-community/Qwen2.5-Coder-7B-Instruct-4bit \
  --data  train/dataset/agc_pairs_v13_dual.jsonl \
  --batch-size 2 --epochs 3 --max-seq 1024 \
  --lora-rank 16 \
  --adapter-path train/lora_adapter_v13_baseline
```

Watch for:
- `iters: ~1584` (3 epochs × ~528 steps).
- val loss starting around 1.3, ending around 0.12-0.13.
- ~75 min wall, ~12 GB peak memory.

- [ ] **Step 2: Verify the adapter saved.**

```bash
ls train/lora_adapter_v13_baseline/
```
Expected: `adapters.safetensors`, ~15 intermediate `00001NN_adapters.safetensors` files, `adapter_config.json`.

- [ ] **Step 3: Save and verify on disk.**

```bash
du -sh train/lora_adapter_v13_baseline/
```
Expected: ~50-100 MB at LoRA rank 16.

---

## Task 14: P5b — Train v14 with extended tokenizer

**Files:**
- Output: `train/lora_adapter_v14_tokenizer/`

Same hyperparameters as Task 13. Only the model (patched base) and the tokenizer differ.

- [ ] **Step 1: Kick off v14 training (~75 min wall).**

```bash
python3 train/run_l3.py \
  --model ~/.cache/agc/qwen-7b-extended-v1 \
  --data  train/dataset/agc_pairs_v13_dual.jsonl \
  --batch-size 2 --epochs 3 --max-seq 1024 \
  --lora-rank 16 \
  --train-new-embeddings \
  --adapter-path train/lora_adapter_v14_tokenizer
```

Note the `--train-new-embeddings` flag: critical so the new embedding rows actually learn during fine-tune (mitigation for spec R2).

- [ ] **Step 2: After ~50 iters, sanity-check that loss is decreasing.**

```bash
ls -la train/lora_adapter_v14_tokenizer/
```
Should see `0000050_adapters.safetensors` or similar early checkpoint. Inspect any training log for "val loss" trend.

If val loss is **diverging** (going UP steadily) or stuck at the initial value: stop the run; the new-embedding training isn't catching. Diagnose `--train-new-embeddings` wiring before continuing.

- [ ] **Step 3: Wait for full run completion.**

Final val loss should land near the v13 baseline (~0.12-0.13). If meaningfully worse (>0.20), the patched base or new tokenizer is hurting trainability — record the result, proceed to eval anyway, but expect this to show in pass rate.

- [ ] **Step 4: Verify the adapter saved.**

```bash
ls train/lora_adapter_v14_tokenizer/
```
Expected: same structure as v13 baseline.

- [ ] **Step 5: Save and verify on disk.**

```bash
du -sh train/lora_adapter_v14_tokenizer/
```

---

## Task 15: P6a — Eval v13 baseline cascade

**Files:**
- Output: `bench/results/2026-05-08/agc-v13-baseline-cascade.jsonl`

- [ ] **Step 1: Create the results directory.**

```bash
mkdir -p bench/results/2026-05-08
```

- [ ] **Step 2: Run the cascade eval on v13 baseline.**

```bash
python3 train/eval_checkpoint_cascade.py \
  --model mlx-community/Qwen2.5-Coder-7B-Instruct-4bit \
  --base-adapter train/lora_adapter_v13_baseline \
  --checkpoints adapters,0001000_adapters,0000500_adapters \
  --prompt-format no_tests \
  --n 8 --temp 0.7 \
  --out bench/results/2026-05-08/agc-v13-baseline-cascade.jsonl
```

Wall: ~30 min.

- [ ] **Step 3: Read the final summary.**

The script prints `total tok_in: ...`, `total tok_out: ...`, plus pass count.

Record:
- `pass_count` (X/30)
- `total_in`, `total_out`
- `tok_per_pass = (total_in + total_out) / pass_count`

- [ ] **Step 4: Verify result file.**

```bash
ls -la bench/results/2026-05-08/agc-v13-baseline-cascade.jsonl
wc -l bench/results/2026-05-08/agc-v13-baseline-cascade.jsonl
```
Expected: file present, 30 lines (one per problem).

- [ ] **Step 5: Save and verify on disk.**

```bash
python3 -c "
import json
with open('bench/results/2026-05-08/agc-v13-baseline-cascade.jsonl') as f:
    rows = [json.loads(l) for l in f]
passed = sum(1 for r in rows if r.get('passed'))
tot_in = sum(r.get('tokens_in', 0) for r in rows)
tot_out = sum(r.get('tokens_out', 0) for r in rows)
print(f'v13 baseline: {passed}/{len(rows)}, tok/pass = {(tot_in + tot_out) / max(passed, 1):.0f}')
"
```

---

## Task 16: P6b — Eval v14 with secondary token counts

**Files:**
- Output: `bench/results/2026-05-08/agc-v14-tokenizer-cascade.jsonl`

This eval uses the **patched base + v14 adapter** as primary, and the **original Qwen tokenizer** as secondary, so the result file contains both Y (active-vocab) and Z (re-encoded) token counts per problem.

- [ ] **Step 1: Run the cascade eval with secondary tokenizer.**

```bash
python3 train/eval_checkpoint_cascade.py \
  --model ~/.cache/agc/qwen-7b-extended-v1 \
  --base-adapter train/lora_adapter_v14_tokenizer \
  --checkpoints adapters,0001000_adapters,0000500_adapters \
  --prompt-format no_tests \
  --n 8 --temp 0.7 \
  --secondary-tokenizer mlx-community/Qwen2.5-Coder-7B-Instruct-4bit \
  --out bench/results/2026-05-08/agc-v14-tokenizer-cascade.jsonl
```

Wall: ~30 min.

- [ ] **Step 2: Verify result file has both primary and secondary token counts.**

```bash
python3 -c "
import json
rows = [json.loads(l) for l in open('bench/results/2026-05-08/agc-v14-tokenizer-cascade.jsonl')]
sample = rows[0]
print('Keys present:', sorted(sample.keys()))
assert 'tokens_in' in sample and 'tokens_in_secondary' in sample
assert 'tokens_out' in sample and 'tokens_out_secondary' in sample
print(f'sample tokens_in primary={sample[\"tokens_in\"]} secondary={sample[\"tokens_in_secondary\"]}')
"
```
Expected: both fields present; secondary count > primary count (because Qwen splits AGC tokens into more pieces — confirms the new tokenizer is compressing).

- [ ] **Step 3: Compute Y, Z, and X aggregate metrics.**

```bash
python3 -c "
import json
v13 = [json.loads(l) for l in open('bench/results/2026-05-08/agc-v13-baseline-cascade.jsonl')]
v14 = [json.loads(l) for l in open('bench/results/2026-05-08/agc-v14-tokenizer-cascade.jsonl')]

def stats(rows, key_in='tokens_in', key_out='tokens_out'):
    passed = sum(1 for r in rows if r.get('passed'))
    tot = sum((r.get(key_in, 0) or 0) + (r.get(key_out, 0) or 0) for r in rows)
    return passed, tot, tot / max(passed, 1)

p13, t13, ppass13 = stats(v13)
p14_Y, t14_Y, ppass14_Y = stats(v14, 'tokens_in', 'tokens_out')
p14_Z, t14_Z, ppass14_Z = stats(v14, 'tokens_in_secondary', 'tokens_out_secondary')

print(f'X (v13 baseline, old vocab):    {p13}/30  tok/pass = {ppass13:.0f}')
print(f'Y (v14, new vocab — paid):      {p14_Y}/30  tok/pass = {ppass14_Y:.0f}')
print(f'Z (v14, re-encoded as old):     {p14_Z}/30  tok/pass = {ppass14_Z:.0f}')
print(f'Claude oneshot baseline:        30/30  tok/pass = 392')
print()
print(f'Verification premium (Y vs 392): {ppass14_Y/392:.2f}x')
print(f'Compression ratio (Y vs Z):      {ppass14_Y/max(ppass14_Z,1):.2f}x')
"
```

- [ ] **Step 4: Add provenance sidecar.**

```bash
python3 -c "
import json
from pathlib import Path
prov = {
    'tokenizer': 'qwen-extended-v1',
    'tokenizer_path': 'train/agc_tokenizer',
    'tokenizer_added_tokens': len(json.loads(Path('train/agc_tokenizer/new_token_id_map.json').read_text())),
    'base_model': 'qwen-7b-extended-v1',
    'adapter': 'train/lora_adapter_v14_tokenizer',
    'token_count_method_primary': 'tokenizer.encode (new vocab)',
    'token_count_method_secondary': 'tokenizer.encode (qwen-original) on same strings',
    'claude_baseline_method': 'char/3.5 estimate',
    'date': '2026-05-08',
}
out = Path('bench/results/2026-05-08/agc-v14-tokenizer-cascade.provenance.json')
out.write_text(json.dumps(prov, indent=2))
print('Wrote', out)
"
```

- [ ] **Step 5: Save and verify on disk.**

```bash
ls -la bench/results/2026-05-08/
```

---

## Task 17: Decide outcome — sub-parity or escalate

**Files:**
- Output (if needed): `bench/results/2026-05-08/agc-v14-failure-analysis.md`

Apply the spec's decision rule based on Task 16's metrics.

- [ ] **Step 1: Write down the four numbers from Task 16, Step 3.**

| Number | Value | Meaning |
|---|---|---|
| X (v13 baseline, old vocab) |  | Clean A/B anchor |
| Y (v14 new vocab, paid) |  | Inference-cost number |
| Z (v14 re-encoded old vocab) |  | Apples-to-apples vs X |
| Claude baseline | 392 | Reference target |

- [ ] **Step 2: Apply the headline-decision rule.**

- **If `Y < 392` AND v14 pass rate ≥ 30/30:** SUB-PARITY ACHIEVED. Headline this. Update `plan.md` with the new state (verification premium < 1.0x). Skip Step 3-4 (no escalation needed).
- **If `Z` beats `X` by 10% or more AND v14 pass rate ≥ 30/30:** Bonus claim — the new tokenizer also nudges the model toward shorter source. Worth a paper paragraph.
- **If neither (`Y ≥ 392` and v14 pass rate < 30/30):** Negative result. Continue to Step 3.

- [ ] **Step 3 (if escalating): Try one retry at vocab=300.**

Re-run Tasks 10, 11, 12, 14, 16 with `--vocab-size 300` in Task 10. If the smaller vocab yields a positive result, document and move on. If still negative, continue to Step 4.

- [ ] **Step 4 (if both attempts fail): Document negative result, move to L6.**

Write `bench/results/2026-05-08/agc-v14-failure-analysis.md` with:
- Final X, Y, Z numbers from both attempts.
- Top 3 hypotheses for why merge-init didn't catch.
- Note that tokenizer artifacts (`train/agc_tokenizer/`, `~/.cache/agc/qwen-7b-extended-v1/`) are preserved for future work.

Then update `plan.md`: mark L7 as ATTEMPTED-NEGATIVE, promote L6 to ACTIVE.

- [ ] **Step 5: Save and verify on disk.**

```bash
ls -la bench/results/2026-05-08/
```

---

## Self-review (run before declaring plan complete)

**Spec coverage check:**

| Spec section | Plan task(s) |
|---|---|
| Architecture (vocab extension, merge-init) | Tasks 5, 6 (math + pipeline) |
| C1 corpus extractor | Task 2 |
| C2 BPE trainer + whitelist | Task 3 |
| C3 vocab extender | Task 4 |
| C4 model patcher | Tasks 5 & 6 |
| C5 retrain + eval | Tasks 8 (run_l3 modification), 13, 14, 15, 16 |
| Phase ordering P1-P6 | Tasks 9-16 |
| Measurement methodology (Y, Z, X) | Tasks 7, 16 |
| Provenance stamping | Task 16, Step 4 |
| R1 tied-embeddings smoke test | Task 12, Steps 2-4 |
| R2 train-new-embeddings flag | Task 8, Task 14 Step 1 |
| R3 whitelist filter | Task 3 |
| R4 re-quantization smoke test | Task 12, Step 2 |
| R5 mlx-lm tokenizer integration | Task 6 (colocate tokenizer with model) |
| Escalation rule (one retry at vocab=300) | Task 17 |
| v13 baseline (clean A/B) | Tasks 13, 15 |
| Prerequisite: v13 dual corpus | Task 0 |

**Placeholder scan:** Searched the plan for "TBD", "TODO", "fill in", "implement later", "similar to" — none found.

**Type / signature consistency:** `merge_init_row(constituent_ids, embedding_matrix)` — signature consistent across Task 5 (definition + tests) and Task 6 (caller). `is_valid_agc_token(token, stdlib)` — consistent across Task 3 and Task 4 (caller). `count_with_tokenizer(tok, text)` — consistent across Task 7 tests and impl. `filter_new_tokens(candidates, qwen_vocab)` and `assign_new_ids(candidates, start_id)` — consistent across Task 4. New token IDs always start at 151,665 (unified across spec, Task 4, Task 12).

**File path consistency:** `train/agc_tokenizer/` for the extended tokenizer dir. `~/.cache/agc/qwen-7b-extended-v1/` for the patched base. `train/lora_adapter_v13_baseline/` and `train/lora_adapter_v14_tokenizer/` for the two adapters. `bench/results/2026-05-08/agc-{v13-baseline,v14-tokenizer}-cascade.jsonl` for results. All consistent.

Plan ready for execution.
