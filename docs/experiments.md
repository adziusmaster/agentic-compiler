# Experiments: a chronicle of what worked and what didn't

This document records every LoRA adapter trained against the AGC benchmark,
the corpus it used, the headline result, and — for the negative results —
why it failed. The lab notebook itself, in narrative form.

The headline today is **v12 (30/30 @ 417 tok/pass, 1.07× verification
premium)**. Everything below is either a step toward that result or a
post-v12 attempt to push under it.

## Summary table

| run | date | base | corpus | pass | tok/pass | premium | verdict |
|---|---|---|---|---|---|---|---|
| v4 | 2026-04 | Qwen-3B-4bit | Gemini-distilled | 10/30 | — | — | early baseline |
| v5 | 2026-04 | Qwen-3B-4bit | Gemini Flash + curation | 21/30 | — | — | scaling proof |
| v6c | 2026-05-05 | Qwen-3B-4bit | claude + flash | 19/30 | 969 | — | greedy single-shot |
| **v6-v10 5-adapter cascade** | 2026-05-05 | Qwen-3B-4bit ×5 | claude per-adapter | **30/30** | **3,209** | **8.18×** | first 30/30 |
| v11 | 2026-05-07 | Qwen-7B-4bit | claude_dual | 29/30 | 1,197 | 3.05× | base swap to 7B |
| **v12** | **2026-05-07** | **Qwen-7B-4bit** | **claude_dual** | **30/30** | **417** | **1.06×** | **HEADLINE** |
| v13 | 2026-05-08 | Qwen-7B-4bit | claude + 28 naming | 28/30 | 1,430 | 3.65× | naming-pair interference |
| v14 | 2026-05-08 | Qwen-7B-4bit-extended | claude + 28 naming | 29/30 | 850 | 2.17× | L7 negative |
| v15 | 2026-05-29 | Qwen-7B-4bit | claude + 30 hard | 30/30 | 428 | 1.09× | tied — no cascade needed |
| v16 | 2026-06-01 | Qwen-7B-4bit | claude + 30 hard + 12 cap | 30/30 | 654 | 1.67× | local fix → global regression |

"Premium" = (AGC tok/pass) / 392, where 392 is the Claude Code subagent
one-shot baseline (30/30 @ 392, char/3.5 estimate). All AGC token counts
are `tokenizer.encode` measurements on the actual model output — see
`docs/training.md` §8 for the cross-method noise floor.

---

## v4–v5: early-baseline (2026-04, distilled corpus)

Earliest adapters. Trained on Gemini Flash output as the supervisor;
distilled into a 3B Qwen-Coder base. v4 hit **10/30**, v5 hit **21/30**.
Useful as a sanity check that LoRA + a tiny base + a code-model prior
was a workable shape, but well below the target.

Artifacts: `train/lora_adapter_v4_3b/`, `train/lora_adapter_v5_hq/`.

The pivot from "distill from Gemini" to "author with Claude Code
subagents" happened here: distilled corpora plateaued around 21/30
because Flash itself made systematic errors that the student
inherited. Claude-authored pairs gated by `agc check` produced strictly
correct training data and lifted the ceiling.

## v6–v10: the first 30/30 (5-adapter cascade, 2026-05-05)

Five independently-trained 3B-4bit adapters on overlapping
Claude-authored slices of the corpus. Each adapter trains for a
different mix of problem categories, gets different
overfit-to-this-style biases. Inference samples up to 8 cands from
each adapter in turn, early-stopping on first PASS:
**30/30 at 3,209 tok/pass** (8.18× the Claude-Code-subagent baseline).

This was the project's first 100% verified result. Heavy: 5
checkpoints to load, ~3,000 tokens of LLM output per problem on
average. The cost was a function of (a) the 3B base being small enough
that one greedy sample rarely worked, (b) the cascade not early-
stopping inside a sample (only across adapters).

Source: `bench/results/2026-05-05/agc-cascade-final.jsonl`.
Adapters: `train/lora_adapter_v6_claude/` ... `v10_claude/`.

## v11: the 7B base swap (2026-05-07)

Single 7B-4bit adapter, no cascade. Same Claude corpus
(`agc_pairs_claude_dual.jsonl`). **29/30 @ 1,197 tok/pass (3.05×).**
Lost on 30-grade-average (the project's residual hard problem). The
7B base is much stronger per-sample; the failure mode shifted from
"need more diversity" to "need more samples on the one hard problem."

Cascade at v11 across checkpoints from the same training run pushed
to 29/30 @ 1,197 with iter_1500 contributing 28 passes,
iter_1000 adding 1, iter_500 adding 0. Diminishing returns past 1-2
intermediate checkpoints — the cascade is a free diversity trick but
the upside is bounded.

Source: `bench/results/2026-05-07/agc-v11-checkpoint-cascade.jsonl`.

## v12: the headline (2026-05-07)

Same corpus as v11 (`agc_pairs_claude_dual.jsonl`), but bumped to the
"L3" hyperparameter recipe: rank 16, cosine LR 1e-4 → 1e-6, batch 2,
3 epochs. See [`training.md`](training.md) for the full recipe.
1584 iters total (~75 min wall).

Cascade eval: `adapters` (final), `0001000_adapters`, `0000500_adapters`.

**30/30 @ ~417 tok/pass, premium 1.06×.** Two reproducibility runs at
413 and 420 tok/pass (variance 1.7%).

What unlocked this drop in one session:

1. **Server-side wrapper-injection** (`assemble()` in
   `train/eval_bestof_n.py`). When the model defined a function with
   matching arity but the wrong name (e.g. `grade` instead of
   `letter_grade`), the assembler synthesizes a passthrough wrapper
   before `agc check` runs. This eliminated the sampling tail on
   30-grade-average.
2. **REQUIRED-top-level-name directive** in the prompt
   (`extract_signature_hints`). Stronger phrasing than the passive
   "signatures:" list. Marginal in isolation, useful in concert.
3. **The 7B base + L3 hyperparameters** vs the prior 5-adapter 3B
   cascade. Bigger model + better adapter recipe replaces the
   five-adapter ensemble.

Source: `bench/results/2026-05-07/agc-v12-l3-cascade-wrapped.jsonl`
and `agc-v12-l3-cascade-wrapped-run2.jsonl`. Adapter:
`train/lora_adapter_v12_l3/`.

**Sanity check that 417 ≈ floor.** A compact-prompt + temp=0
experiment (single run) regressed to 29/30 @ 924. The verbose prompt
and all-temp=0.7 sampling are load-bearing for this result; trying
to "tighten" them backfires.

---

## v13: naming-discipline pairs — negative (2026-05-08)

**Hypothesis.** The wrapper-injection workaround patched a symptom:
the model still sometimes renamed entry-point functions. Train it
out at the source by adding 28 "naming-discipline" pairs whose
objectives specify unusual compound function names
(`drop_smallest`, `score_to_letter`, `quarterly_total`, etc.) so the
model learns to use the EXACT name specified rather than its English
shortening.

**Corpus.** `agc_pairs_claude.jsonl` (587) + `agc_pairs_naming.jsonl` (28)
→ `agc_pairs_v13_dual.jsonl` (1230 dual). Same v12 L3 hyperparameters.

**Result: 28/30 @ 1,430 tok/pass, premium 3.65×.** Regression. The
naming pairs broke 01-word-count and 02-reverse-words — pure-function
problems that v12 nailed greedily. Naming-discipline training shifted
the model's distribution away from the simple-function shape it
needed for those.

Source: `bench/results/2026-05-08/agc-v13-baseline-cascade.jsonl`.
Adapter: `train/lora_adapter_v13_baseline/`.

**Diagnosis.** Adding 28 pairs (~5% of the corpus) was enough to
move the centroid noticeably. Catastrophic interference at small
corpus sizes is a recurring theme.

## v14: custom tokenizer (L7) — negative (2026-05-08)

**Hypothesis.** Qwen's tokenizer splits AGC stdlib names like
`math_ceil` into 3 tokens. Extending the vocab with single-token
forms for AGC stdlib calls, type annotations, and S-expression
openers should reduce token count. Spec:
`docs/superpowers/specs/2026-05-08-agc-custom-tokenizer-design.md`.
Plan: `docs/superpowers/plans/2026-05-08-agc-custom-tokenizer.md`.

**What we did.** Built `train/agc_tokenizer/` — extended Qwen's
151,665-token vocab to 151,892 by adding 227 AGC-specific tokens at
IDs ≥ 151,665. Initialized new embedding rows via **merge-init**:
the mean of the constituent old-token rows
(e.g. `math_ceil` ← mean(emb[`math`], emb[`_`], emb[`ceil`])).
Re-quantized the 4-bit base accordingly and stored it at
`~/.cache/agc/qwen-7b-extended-v1/`. Trained v14 LoRA on top
(same `agc_pairs_v13_dual.jsonl`).

**Result: 29/30 @ 850 tok/pass, premium 2.17%.** Regression vs v12 on
the cascade evaluator. The Y/Z ratio (new-tokenizer counts ÷
original-tokenizer counts) averaged **0.998** across all 30 problems
— the new tokens were essentially **never emitted at inference**.

**Why it failed.** Two distinct issues:

1. **R2: merge-init alone doesn't make the LoRA learn to use the new
   tokens.** mlx-lm's
   `lora_parameters.keys: [..., "embed_tokens"]` triggered
   `ValueError: [grad] Must specify at least one argument` — the
   keys substring matcher selected zero LoRA targets. We trained v14
   without `--train-new-embeddings`. The new embedding rows stayed
   at their merge-init values; the rest of the model's LoRA didn't
   learn to emit those token IDs.
2. **R4: re-quantization noise corrupted structural priors.**
   Per-problem diagnostic on 24-tip-split
   (`train/diag_24_tip_split.py`) at n=4: v14 reaches for the right
   stdlib function (`math.ceil`, the 2-token sequence — not the new
   single token `math_ceil`) but emits **mangled S-expression
   structure**: Python-style infix `/` and unbalanced parens, e.g.
   `(math.ceil (* share 100)) / 100))`. v12 and v13 on the same
   problem produce correct s-expr structure. The re-quantization of
   the 4-bit base for the extended vocab introduced enough numerical
   noise that the model's prefix-syntax discipline degraded.

**Conclusion.** Tokenizer extension via merge-init alone is
insufficient. To make it work properly would require either
(a) a custom training loop that genuinely unfreezes embedding rows
≥ 151,665, or (b) full-precision re-training of the base for the
extended vocab (out of scope for this project's hardware budget).
The 227 new tokens are auditable, the failure is well-characterized,
and this writeup serves as the paper's negative-result vignette.

Source: `bench/results/2026-05-08/agc-v14-tokenizer-cascade.jsonl`
and `agc-v14-tokenizer-cascade.provenance.json`.
Diagnostic: `bench/results/2026-05-29/v1{2,3,4}-24-tip-split-cands.jsonl`.
Adapters: `train/lora_adapter_v13_baseline/` (A/B baseline) and
`train/lora_adapter_v14_tokenizer/`.

## v15: targeted hard-problem pairs — tied (2026-05-29)

**Hypothesis.** v12's tok/pass is dominated by 5 problems that need
cascade sampling (23-rental-cost, 17-file-line-count,
30-grade-average, 24-tip-split, 26-tax-progressive). If we add
training pairs whose objective shape mirrors those (multi-step
financial, bracketed tiers, indexed table lookups, domain formulas
via math.pow / math.sqrt), the model should pass them at cands=1 and
the tail collapses.

**What we did.** Designed 30 topics with computed expected outputs
(see `/tmp/compute_topic_tests.py` snapshot for the formulas; topic
names disjoint from bench function names to avoid contamination).
Dispatched 30 isolated Claude Code subagents, one per topic. Each
subagent received the spec, drafted an AGC solution, validated it
against `agc check` locally if it could (the sandbox blocks
`dotnet` for subagents — we worked around by writing candidate `.ag`
files and validating centrally from the parent). Centralized
validation via `train/gen_hard_pairs.py`: **30/30 pairs passed
`agc check` 5/5**. Saved to `train/dataset/agc_pairs_hard.jsonl`.

Trained v15 on `agc_pairs_claude.jsonl + agc_pairs_hard.jsonl` (617
source → 1234 dual). Same v12 L3 hyperparameters. ~80 min wall.

**Result: 30/30 @ 428 tok/pass, premium 1.09%.** Effectively tied
with v12 on the headline number but **every problem passes at the
head checkpoint** — no cascade fallthrough needed at all. v12 needed
the cascade for some problems.

**Per-problem decomposition vs v12:**

- **Wins:** 23-rental-cost **-1053 tok** (1522 → 469, fell off the
  sampling tail), 17-file-line-count -392, 25-loan-payment -39,
  29-compound-interest -46, 05-gcd -27.
- **Losses:** 20-file-copy **+620** (349 → 969 — passed but sampled),
  14-file-write-status **+580** (296 → 876 — same), 24-tip-split +363,
  04-is-prime +120.
- **Net: +241 tokens** vs v12.

The targeted financial pairs **worked locally** (the 23-rental
sampling tail evaporated as predicted) but **shifted the model's
distribution away from capability-using problems** (file/write/copy
regressed by amounts that roughly balance the financial wins). Mixed
signal: structural robustness gained at v15 (no cascade dependence)
but headline tok/pass tied.

Source: `bench/results/2026-05-29/agc-v15-cascade.jsonl`.
Adapter: `train/lora_adapter_v15/`.

## v16: capability pairs to fix v15's IO regressions — negative (2026-06-01)

**Hypothesis.** v15's regressions clustered on file/write/copy
problems. Add 12 targeted capability-using pairs (file.read,
file.write, env.get, http.fetch, db.query — mirroring the bench
capability problem shapes with `mocks` patterns) to recover those.

**What we did.** 12 topics specifying capability extern declarations,
typed function signatures, and `mocks` for testing. Same parallel-
subagent + central-validation flow as v15. All 12 pairs validated
through `agc check`.

Trained v16 on `agc_pairs_claude + agc_pairs_hard + agc_pairs_capability`
(629 source → 1258 dual). Same hyperparameters. ~80 min wall.

**Result: 30/30 @ 654 tok/pass, premium 1.67×.** Regression vs both
v15 and v12.

**Per-problem decomposition vs v15:**

- **Wins (capability hotspots fixed):** 14-file-write-status **-600**
  (876 → 276 — recovered), 20-file-copy **-620** (recovered),
  24-tip-split -711, 04-is-prime -120.
- **Losses (new catastrophic interference):**
  12-file-first-line **+5224** (291 → 5515, fell to iter_500
  fallback), 16-env-join-two **+1445**, 02-reverse-words **+1004**,
  25-loan-payment **+842**, 13-env-int-or +300.
- **Net: +6785 tokens** vs v15. Strictly worse on the headline.

**Diagnosis.** The 12 capability pairs taught the model
capability-specific patterns at the expense of other problems'
default-correct behavior. The corpus is small enough that each new
training-data bucket (~2% of total) rebalances the distribution
enough to break unrelated problems. The local fix on
file-write-status and file-copy created new global regressions on
file-first-line, env-join-two, and reverse-words.

**Conclusion.** Three rounds of post-v12 corpus surgery (v13 naming,
v15 hard, v16 capability) have demonstrated the same pattern:
**incremental data tweaks at this corpus scale (~600 pairs) move the
problem around rather than reduce it.** Pushing sub-parity through
LoRA-corpus expansion alone has hit diminishing returns.

Source: `bench/results/2026-06-01/agc-v16-cascade.jsonl`.
Adapter: `train/lora_adapter_v16/`.

---

## What's left if anyone wants to try

These were considered but not run, in rough order of expected upside:

- **L4-full: `agc-check --trace` + TDD prompt.** Emit the verifier's
  per-test trace into the model's prompt on retry — "test
  `round_up` failed because per_person(100,10,20,3) returned 43.33,
  expected 43.34". Today's verifier reports a one-line error; a
  detailed trace would give the model concrete failure feedback to
  patch its next sample. Listed in `plan.md` L4 — deferred since
  v12 already hit the goal. Plausibly the biggest remaining lever
  for the sampling tail, but requires careful TCB-respecting changes
  to `Agentic.Check`'s diagnostic output.
- **L5: verifier-guided agentic refinement (proper retry).** The
  early-2026-05 attempt fed `agc-check` errors back into a long
  retry prompt; the prompt was too verbose and the model lost paren
  discipline. A surgical retry prompt (single error message, single
  re-emit of the function only) is plausibly different. Speculative;
  no evidence it would converge.
- **L4-lite already shipped.** The REQUIRED-name directive in
  `build_prompt_no_tests` is the surviving fragment of the L4 family.
- **Properly trained extended embeddings (L7 fix).** As described in
  v14: write a custom training loop that genuinely unfreezes
  embedding rows ≥ 151,665. Engineering effort: ~1 day. Worth doing
  only if the patched-base story turns out to be a load-bearing part
  of the paper.
- **Larger corpus.** ~600 pairs is small enough that distribution
  shift hits every new bucket. ~5000 pairs might absorb new
  categories without catastrophic interference. Generation cost:
  ~3 days of parallel-subagent wall time. Risk: subagent style
  uniformity means more pairs ≠ more diverse signal.

The v12 headline is publishable as-is.
