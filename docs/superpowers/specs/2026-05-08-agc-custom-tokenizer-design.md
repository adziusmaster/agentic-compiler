# Spec: AGC custom tokenizer (L7) — vocab extension on Qwen2.5-Coder-7B

**Status:** approved 2026-05-08, ready for implementation plan.
**Author:** brainstorm session 2026-05-08.
**Project driver:** reduce tokens-per-pass (energy / resource efficiency)
while preserving verified + auditable output. Paper headline secondary.

## Context

AGC currently sits at 30/30 @ ~417 tok/pass (verification premium 1.06×
vs the Claude Code subagent oneshot baseline of 30/30 @ 392). The
v12 cascade with wrapper-injection + REQUIRED-name directive shipped
on 2026-05-07. The plan's stretch goal of 1.8× was already exceeded.

The next lever from `plan.md` was originally L6 (surface-syntax
compression), deferred because it grows the verifier (TCB) — directly
hurting the auditability claim. L7 (custom AGC tokenizer) was deferred
as month-scale and "probably not worth pre-paper."

The framing has shifted: the project's primary driver is now
**energy / resource efficiency for verified high-stakes code**, with
paper headline secondary. Under this framing, L7 is unambiguously a
green-and-auditable win — pure representation change, zero TCB impact —
while L6 trades audit credibility for inference savings. We promote L7
to the active lever and defer L6 conditional on L7's measured outcome.

## Goal

Reduce inference token consumption on the AGC bench by extending Qwen's
tokenizer with AGC-specific tokens, retraining the v14 LoRA adapter on
the extended vocab, and measuring the delta against a clean v13 (old
vocab) baseline trained on the same data with the same hyperparameters.

**Success criteria:**

- v14 reaches **≥ 30/30** on the bench (no pass-rate regression vs v12).
- v14 tok/pass measured in **new-vocab tokens (Y)** is materially lower
  than 392 (Claude Code subagent oneshot baseline) — i.e.
  *sub-parity verification premium*. This is the inference-cost claim.
- v14 tok/pass measured in **re-encoded old-vocab tokens (Z)** is
  reported alongside Y so the comparison is honest. Z ≤ v13's tok/pass
  is the apples-to-apples science number.
- Provenance stamps on the result file name the tokenizer version, the
  added-token count, and the count method for both Y and Z.

If v14 underperforms v13 on either pass rate or Z, the spec covers
**one** retry at vocab=300; two failures end the L7 attempt and the
project moves to L6.

## Non-goals

- Replacing Qwen's tokenizer entirely. We keep the existing 151,665-token
  vocab intact and only *extend* it. The model's English / general-code
  reasoning ability must not regress.
- Changing the AGC language surface syntax. That's L6, deferred.
- Modifying `Agentic.Check` (the TCB). The verifier reads raw AGC source;
  tokenization is invisible to it.
- Comparing against HumanEval / MBPP / non-AGC benchmarks. Out of scope.
- Custom training of the *base* model (we patch only the embedding +
  output layers; the rest of the 7B base stays as-is and is later
  re-quantized to 4-bit).

## Architecture

**Vocab extension, not replacement.** Add ~500 AGC-specific tokens at
IDs ≥ 151,665. Token IDs below stay identical, so the model's prior
knowledge is preserved bit-for-bit. The standard "merge-init" technique
initializes new embedding rows as the **mean of the embeddings of the
old tokens that previously encoded that string**, giving LoRA fine-tune
a warm start.

**Three classes of new tokens** (selected by frequency on AGC-only corpus,
filtered by whitelist to avoid corpus-specific identifiers):

1. AGC stdlib calls as single tokens — `str_substring`, `str_split`,
   `math_floor`, `math_round`, `math_mod`, `list_length`, `dict_get`, …
2. Type annotations as single tokens — `: Num`, `: Str`, `: Bool`,
   `-> Num`, `-> Str`.
3. S-expr openers and operators not already merged by Qwen — `(assert-eq`,
   `(assert-near`, `(extern`, `(let`, `(do` (some are, some aren't —
   BPE training picks the gaps; deduplication step drops anything Qwen
   already has).

**TCB stays untouched.** The verifier doesn't know about tokenization.
Zero LOC change to `Agentic.Check`. Auditability headline survives
intact. This is the design's load-bearing property.

## Components

Each component has one job, a clean interface, and is independently
testable.

### C1. Corpus extractor — `train/build_agc_corpus.py`

Pulls AGC source out of all known sources into a single newline-delimited
text file:

- `solution` field from every `train/dataset/agc_pairs_*.jsonl`
  (~14,484 modules across all current corpora)
- All `bench/problems/*/reference.ag` and `tests.ag` (60 files)
- AGC stdlib `.ag` files (search via `find . -name "*.ag"`,
  exclude bench/* if already included)

**Output:** `train/dataset/agc_corpus.txt` (target ~2-4 MB).
**Test:** `wc -l` shows ~15K modules; spot-check first/last lines parse
as valid AGC (manually, or via `agc-check --parse-only` if available).
**Wall:** <1 min.

### C2. BPE trainer — `train/train_agc_bpe.py`

Uses HuggingFace `tokenizers` library to train a BPE on
`agc_corpus.txt` with `vocab_size=500` and the **same byte-level
pre-tokenizer config Qwen uses** (so merges are byte-compatible with
the existing tokenizer).

**Whitelist filter applied to candidates:**

- Keep: AGC keywords, s-expr openers, stdlib calls (against a curated
  stdlib list extracted from `Agentic.Check` or `bench/problems/*`),
  type annotations, common operators.
- Reject: candidates matching `[a-z_]+` with > 4 chars and no stdlib
  match (almost certainly a user-defined identifier from the corpus,
  e.g. `quarterly_total`, `is_even`).

**Output:** `train/agc_tokenizer/merges.json` with up to 500 candidate
tokens + their merge rules.
**Test:** top-50 candidates are obviously AGC syntax. If they're random
byte fragments, the byte-level pre-tokenizer config is wrong.
**Wall:** <2 min.

### C3. Vocab extender — `train/extend_qwen_tokenizer.py`

Loads Qwen's `tokenizer.json`, filters C2 candidates against the
existing 151,665-token vocab, drops duplicates, assigns new IDs starting
at 151,665, writes the full extended tokenizer.

**Output:** `train/agc_tokenizer/tokenizer.json` (HF-compatible; loadable
via `AutoTokenizer.from_pretrained('train/agc_tokenizer')`).
**Decision gate:** print `"X net new tokens added (Y dropped as
duplicates)"`. If `X < 200`, the corpus is too small or BPE settings
need tuning; pause before continuing.
**Test:**
`AutoTokenizer.from_pretrained('train/agc_tokenizer').encode("(defun foo (x: Num) (return x))")`
produces noticeably fewer tokens than the 16-token Qwen baseline.
**Wall:** <1 min.

### C4. Model patcher — `train/patch_model_embeddings.py`

The hardest piece. Loads `Qwen/Qwen2.5-Coder-7B-Instruct` (full fp16,
~14 GB), checks `config.json:tie_word_embeddings`, expands
`embed_tokens` (and `lm_head` if untied) with merge-init rows
(mean of constituent old embeddings for each new token).

**Re-quantization:** patched model is re-quantized to 4-bit. Saves to
`~/.cache/agc/qwen-7b-extended-v1/`, with the extended `tokenizer.json`
copied alongside so mlx-lm sees a normal HF model dir.

**Smoke tests (must all pass before P5):**

1. Load patched base, run greedy `generate("Hello world")` — output is
   coherent English (re-quantization didn't break general reasoning).
2. Encode an AGC snippet with the new tokenizer; do one forward pass;
   logits over the new tokens are non-NaN, non-zero.
3. Tied-embedding sanity: if `tie_word_embeddings=True`, only one matrix
   was patched; if `False`, both `embed_tokens` and `lm_head` were
   patched in lockstep (verify by checking the new rows in each).

**Fallback if re-quantization breaks generation:** keep `embed_tokens`
and `lm_head` in fp16, rest of model stays 4-bit (mixed-precision MLX
config). +~500 MB on disk, fully robust. ~30 min to swap.

**Wall:** ~5-10 min for download + patch + quantize, plus smoke tests.

### C5. Adapter training + eval — extend existing scripts

Reuse `train/run_l3.py` and `train/eval_checkpoint_cascade.py` with
flag thread-throughs:

- `--model ~/.cache/agc/qwen-7b-extended-v1` — points at the patched base
- `--tokenizer train/agc_tokenizer` (or implicit if tokenizer is colocated
  with the model)
- `--train-new-embeddings` — **critical flag**: in addition to LoRA's
  default `q_proj`/`v_proj` updates, mark embedding rows ≥ 151,665 as
  trainable so the model actually learns to use the new tokens. Without
  this, merge-init alone may not be enough in 75 min of LoRA.

**Hyperparameters (held identical between v13 and v14 for clean A/B):**

- LoRA rank 16, cosine LR 1e-4 → 1e-6, 50-iter warmup
- batch 2, 3 epochs (~1,584 iters), max_seq 1024
- Same dual training corpus (`agc_pairs_v13_dual.jsonl` — to be built
  per the 2026-05-08 RESUME-POINT in `plan.md`)

**Output adapters:**

- `train/lora_adapter_v13_baseline/` — old vocab, identical data
- `train/lora_adapter_v14_tokenizer/` — new vocab, same data

**Wall:** ~75 min per training run × 2 = ~2.5 hr.

## Training plan & data flow

```
agc_corpus.txt ──→ C2 ──→ merges.json ──→ C3 ──→ tokenizer.json ──┐
                                                                    ├─→ C5 (train v14) ──→ v14 adapter
qwen-7b-fp16   ──────────────→ C4 ──→ qwen-7b-extended ────────────┘                          │
                                                                                               ↓
                                                                                     eval_cascade ──→ tok/pass

qwen-7b-4bit (existing)              ──→ C5 (train v13 baseline)  ──→ v13 adapter ──→ eval_cascade
```

**Phases (each phase has a measurable output before moving on):**

| Phase | What | Output | Wall |
|---|---|---|---|
| P1 | Build corpus (C1) | `agc_corpus.txt` ~2-4 MB, ~15K modules | <1 min |
| P2 | Train BPE on AGC-only (C2) | `merges.json` with ≤500 candidates | <2 min |
| P3 | Extend Qwen vocab (C3) | `tokenizer.json` extended; gate at X≥200 | <1 min |
| P4 | Patch model embeddings (C4) | `qwen-7b-extended-v1/` + smoke tests pass | ~10 min |
| P5a | Train v13 baseline (old vocab) | `lora_adapter_v13_baseline/` | ~75 min |
| P5b | Train v14 (new vocab) | `lora_adapter_v14_tokenizer/` | ~75 min |
| P6a | Eval v13 cascade | `agc-v13-baseline-cascade.jsonl` | ~30 min |
| P6b | Eval v14 cascade | `agc-v14-tokenizer-cascade.jsonl` | ~30 min |

**Total session wall:** ~4 hours.

**A/B integrity:** the only thing that changes between v13-baseline and
v14-tokenizer is the tokenizer + the patched base. Same data, same
prompt template, same hyperparams, same harness. Any tok/pass delta is
attributable to the tokenizer alone.

## Measurement methodology

The honest measurement of "how much did the tokenizer save" is
non-trivial because we're changing the unit of measurement itself.

**The trap:** if v12 is at 417 tok/pass measured with Qwen's tokenizer
and v14 reports 320 tok/pass measured with the extended tokenizer, the
comparison is meaningless — different unit, different number, no real
conclusion.

**The fix: report three numbers per result, not one.**

| Number | Definition | What it tells us |
|---|---|---|
| **Y** | tok/pass counted with the **new** (extended) tokenizer | Inference cost — what the model actually pays at runtime. The energy / efficiency claim. |
| **Z** | tok/pass counted by **re-encoding** v14's prompts+outputs **with the original Qwen tokenizer** | Apples-to-apples vs v13 / v12. The science number. |
| **X** | tok/pass for v13 / v12 with the **old** tokenizer | The baseline. |

If `Z < X`, the new tokenizer didn't just compress — it changed the
model's behavior toward shorter source.
If `Z ≈ X` and `Y ≪ X`, the win is purely representational (still good
under the green framing).
If neither, no win; clean negative result, document it, move to L6.

**Claude oneshot baseline (392) stays anchored at its own units (char/3.5).**
Already a ~10–15% noise floor vs `tokenizer.encode`; we keep that caveat
documented in result provenance.

**Provenance stamping** (extends the existing pattern in `bench/results/`):

```json
{
  "tokenizer": "qwen-extended-v1",
  "tokenizer_path": "train/agc_tokenizer",
  "tokenizer_added_tokens": "<actual count from C3>",
  "base_model": "qwen-7b-extended-v1",
  "adapter": "train/lora_adapter_v14_tokenizer",
  "token_count_method_primary": "tokenizer.encode (new vocab)",
  "token_count_method_secondary": "tokenizer.encode (qwen-original) on same strings",
  "claude_baseline_method": "char/3.5 estimate"
}
```

**Bench harness change required:** `eval_checkpoint_cascade.py`
currently records `tok_in` / `tok_out` from the active tokenizer only.
Add a parallel pass that re-encodes the same generated strings with the
original Qwen tokenizer and writes both counts. ~15 LOC.

**Decision rule on the headline:**

- If **Y < 392**, we have **sub-parity verification premium**. Ship as
  the headline.
- If **Z** beats v13 by 10%+, the new tokenizer also nudges the model
  toward shorter source — bonus claim.
- If neither, document the negative result; move to L6.

## Risks & mitigations

Five risks ordered by likelihood × impact. Each has a fallback so the
project doesn't block on any single one.

### R1. `tie_word_embeddings` quirk in Qwen

If tied, patching `embed_tokens` covers `lm_head` for free. If untied,
both must be patched in lockstep. Easy to get wrong silently — model
runs but new tokens are never *generated* (only consumed).

- **Mitigation:** P4 smoke test 3 asserts new rows present in both
  matrices when untied. After a tiny LoRA warmup (~50 iters), prompt
  the model and confirm the output contains at least one new token ID.
- **Cost if missed:** wasted 75 min training run with diagnostic
  "v14 token counts indistinguishable from v13."

### R2. Merge-init isn't strong enough — embeddings never learn

LoRA only updates `q_proj` / `v_proj` by default, not the embedding
layer. Without explicit unfreezing of new embedding rows, the model
falls back to old multi-token sequences, compression is wasted.

- **Mitigation:** in C5, `--train-new-embeddings` flag in `run_l3.py`
  marks rows ≥ 151,665 as trainable while the rest of the embedding
  matrix stays frozen.
- **Cost if missed:** v14 looks identical to v13; ~3 hr wasted but
  diagnostic is obvious (token counts in `agc-v14-tokenizer-cascade.jsonl`
  match v13 closely).

### R3. BPE candidates dominated by training-corpus-specific identifiers

Frequent corpus identifiers (`is_even`, `quarterly_total`, `peak_day`)
get picked as merges, overfitting vocab to identifiers that don't appear
on bench. Wasted vocab slots.

- **Mitigation:** whitelist filter in C2 (described above) rejects
  candidates that look like user-defined identifiers without stdlib
  match.
- **Cost if missed:** real win drops from ~20% to ~14%. Still a win.

### R4. Re-quantization loss after embedding patching

After patching to fp16 + re-quantizing the whole model to 4-bit,
weights diverge slightly from original Qwen 4-bit. Model may lose 1-2%
on simple tasks just from re-quantization noise.

- **Mitigation:** P4 smoke test 1 (greedy generate on plain English).
  If degraded, fall back to mixed-precision: `embed_tokens` and `lm_head`
  stay fp16, rest of model 4-bit. +~500 MB on disk, fully robust.
- **Cost if missed:** caught at smoke test, swap to mixed-precision,
  +~30 min.

### R5. mlx-lm `AutoTokenizer` integration with extended tokenizer

mlx-lm assumes the tokenizer at the model dir matches the model.
Pointing it at a custom tokenizer dir while loading the model from
another path may need plumbing.

- **Mitigation:** copy patched 4-bit weights AND extended `tokenizer.json`
  into the same directory (`~/.cache/agc/qwen-7b-extended-v1/`). mlx-lm
  then sees a normal HF model dir.
- **Cost if missed:** 1 hr of debug; not blocking.

### Open question (escalation rule)

If v14 is worse than v13 on either pass rate or Z:

1. **One retry at vocab=300** (smaller, lower-risk vocab) before declaring
   the L7 attempt failed.
2. If retry also fails: document the negative result in
   `bench/results/<date>/agc-v14-failure-analysis.md`, leave the patched
   base + tokenizer artifacts in place for future work, move to L6.

Two failed runs = real signal that merge-init or vocab strategy isn't
catching. Step back, don't keep iterating.

## File list (rough)

**New files:**

- `train/build_agc_corpus.py` (C1)
- `train/train_agc_bpe.py` (C2)
- `train/extend_qwen_tokenizer.py` (C3)
- `train/patch_model_embeddings.py` (C4)
- `train/agc_tokenizer/` (output dir for C2 + C3)
- `train/lora_adapter_v13_baseline/` (output dir for v13 retrain)
- `train/lora_adapter_v14_tokenizer/` (output dir for v14)
- `~/.cache/agc/qwen-7b-extended-v1/` (output of C4)
- `bench/results/2026-05-08/agc-v13-baseline-cascade.jsonl`
- `bench/results/2026-05-08/agc-v14-tokenizer-cascade.jsonl`

**Modified files:**

- `train/run_l3.py` — `--train-new-embeddings`, `--tokenizer` flag
  thread-through.
- `train/eval_checkpoint_cascade.py` — secondary token-count pass via
  original Qwen tokenizer (~15 LOC).
- `plan.md` — promote L7 to active, sequence L6 conditional on L7
  outcome (separate edit, not part of this spec but companion to it).

**Untouched (load-bearing):**

- All of `Agentic.*` — TCB stays at ~1153 LOC.
- `bench/run.py`, `bench/problems/*` — bench harness and problem set
  unchanged.
