# Training pipeline

This document describes how the v12 headline adapter is produced end-to-end:
the corpus, the LoRA hyperparameters, the dual-format augmentation, and the
checkpoint-cascade evaluator. Everything below is reproducible from the
artifacts in `train/` and `bench/`.

## 0. What's in the repo vs what you regenerate

LoRA adapter weights (~10 GB across all versions) are **gitignored** —
nothing under `train/lora_adapter_*/` is tracked. Same for the
extended tokenizer (`train/agc_tokenizer/`), the chat-format split
(`train/mlx_chat/`), all `*_dual.jsonl` augmentations, and the
patched 4-bit base (`~/.cache/agc/qwen-7b-extended-v1/`, 4 GB).

What **is** tracked and ships in this repo:

- The source training corpora: `agc_pairs_{claude,naming,hard,capability,pro,self,v4_flash,v5hq_combined,v2_verbose}.jsonl`. The claude (587), hard (30), and capability (12) corpora are the load-bearing inputs.
- The compiler, verifier, and CLI source.
- The bench problems (`bench/problems/`) and the result JSONL files (`bench/results/`).
- All training and evaluation scripts.

To rebuild the v12 headline adapter from a fresh clone (~75 min on
M-series, ≤12 GB peak training memory):

```bash
dotnet build AgenticLanguage.sln -c Debug
python3 train/augment_no_tests.py \
    --in  train/dataset/agc_pairs_claude.jsonl \
    --out train/dataset/agc_pairs_claude_dual.jsonl
python3 train/run_l3.py \
    --model mlx-community/Qwen2.5-Coder-7B-Instruct-4bit \
    --data train/dataset/agc_pairs_claude_dual.jsonl \
    --batch-size 2 --epochs 3 --max-seq 1024 --lora-rank 16 \
    --adapter-path train/lora_adapter_v12_l3
```

Then evaluate with the cascade command from §5 below.

---

## 1. What we're training

A LoRA adapter on top of `mlx-community/Qwen2.5-Coder-7B-Instruct-4bit` —
a 7B-parameter, 4-bit-quantized code model. The adapter is small
(~88 MB per checkpoint, ~1.4 GB across all saved checkpoints from one run)
and stacks on the frozen base at inference time. Rank 16, scale 20,
no dropout.

**Why a small model on top of a small base?** The paper claim is that
verification — not raw model size — is what makes LLM-authored code
trustworthy. The smaller the model, the cleaner the claim. The 7B-4bit
base runs on a 48 GB Apple-silicon laptop with ≤12 GB peak training
memory; inference is local and free.

## 2. The corpus

The headline corpus is `train/dataset/agc_pairs_claude.jsonl` — **587
verified pairs** authored by isolated Claude Code subagents (one
problem per subagent, no shared state, each output validated through
`agc check` before being added). Schema:

```json
{
  "category": "pure" | "naming-discipline" | "hard-multistep" | ...,
  "topic": "<function_name>",
  "objective": "<plain-English specification>",
  "solution": "(module ModName\n  (defun ...)\n  (test ...))",
  "tests_passed": <N>,
  "source": "<who/when generated>"
}
```

The `solution` field is a complete AGC module — both the function
definition(s) AND the embedded `(test ...)` forms. Every solution
has gone through `dotnet Agentic.Cli check <file>` and reported
`(ok (tests-passed N/N))` with N ≥ 3.

### Corpus pieces

| file | pairs | when generated | role |
|---|---|---|---|
| `agc_pairs_claude.jsonl` | 587 | 2026-04..05 (Claude subagents) | the headline source |
| `agc_pairs_naming.jsonl` | 28 | 2026-05-07 | naming-discipline drill (used by v13/v14, *not* by v12) |
| `agc_pairs_hard.jsonl` | 30 | 2026-05-29 | targeted multi-step (v15) |
| `agc_pairs_capability.jsonl` | 12 | 2026-06-01 | targeted file/env/http/db (v16) |
| `agc_pairs_v4_flash.jsonl` etc. | various | early 2026-04 | early Gemini-distilled corpora — historical |

**v12 (the headline) used only `agc_pairs_claude.jsonl`**, augmented
through `augment_no_tests.py` to `agc_pairs_claude_dual.jsonl`
(1174 examples). The other corpora exist for historical/experimental
reasons; see [`experiments.md`](experiments.md).

### Why subagent-authored, not LLM-distilled or hand-authored?

Three reasons:

1. **Independence per pair.** Each Claude Code subagent sees one
   problem, generates a solution, validates it via `agc check`, and
   exits. No cross-pair conditioning, no opportunity to memorize a
   "house style" that the evaluator could also memorize.
2. **Verification at curation time.** Subagents iterate against
   `agc check` locally until a 5/5 pass. Pairs that can't be fixed in
   5 attempts are dropped. The corpus is **strictly correct AGC** —
   the model has nothing wrong to learn.
3. **No human bottleneck.** 587 pairs across diverse topics takes
   hours of wall time with ~30 parallel subagents, not weeks of human
   authoring.

The downside: subagent style is uniform enough that the model can
overfit to "the way Claude writes AGC." This shows up as marginal
benchmark sensitivity to prompt phrasing; see "Limitations" in the
README.

## 3. Dual-format augmentation

For every source pair, `train/augment_no_tests.py` emits **two
training examples**:

- **`format: "with_tests"`** — user prompt asks for a full module
  with embedded tests; assistant response is the original solution.
- **`format: "no_tests"`** — user prompt asks for *defuns only* with
  any required externs but **no `(module …)` wrapper, no `(test …)`
  forms**; assistant response is the same solution with tests stripped.

Why both? At inference time the eval script asks for **defuns only**
(the no-tests format) and **re-attaches tests server-side** before
running `agc check`. Training on both formats teaches the model that
the same `(defun ...)` body is correct in either presentation context,
which keeps it from emitting `(module ...)` wrappers and `(test ...)`
forms at inference (those would just have to be stripped). The
augmentation roughly doubles the training-example count without
needing new source data.

`train/finetune_mlx.py:to_chat()` dispatches on the `format` field
when converting a pair to its chat-template form.

## 4. Hyperparameters (v12's "L3" recipe)

Driver: `train/run_l3.py` → emits `training_config.yaml` → runs
`python -m mlx_lm lora -c <yaml>`.

```yaml
model: mlx-community/Qwen2.5-Coder-7B-Instruct-4bit
fine_tune_type: lora
optimizer: adamw
mask_prompt: true        # only assistant tokens contribute to loss
num_layers: 16           # LoRA target depth — top 16 transformer blocks
batch_size: 2
max_seq_length: 1024
learning_rate: 1.0e-4    # peak LR
lr_schedule:
  name: cosine_decay
  warmup: 50
  warmup_init: 1e-7
  arguments: [1.0e-4, <iters>, 1.0e-6]
lora_parameters:
  rank: 16
  scale: 20.0
  dropout: 0.0
```

For v12: 1174 dual examples → 90/10 train/valid split → 1056 training
examples → `iters = 3 epochs × 1056 / 2 = 1584`. Wall time on
48 GB M-series: ~75 minutes. Peak training memory: ~12 GB.

**Checkpoints are saved every 100 iters** (`save_every: 100`). This
gives the cascade evaluator multiple snapshots of the same training
run with different overfit-vs-coverage trade-offs — see §6.

### Why these specific knobs

- **`mask_prompt: true`.** The user prompt is fixed per-problem (the
  same template across all examples); training on it wastes gradient
  signal. Mask it.
- **`rank: 16, scale: 20`.** Higher rank than the mlx-lm default
  (rank 8). The 7B base needs the extra capacity to fit AGC's
  S-expression syntax without trampling its general code priors. Scale
  scales the LoRA contribution at inference; 20 was empirically the
  sweet spot in early sweeps.
- **`num_layers: 16` (top 16 transformer blocks).** Touching all 32
  blocks didn't help and slowed training; the top 16 carry most of
  the syntax-shaping signal.
- **Cosine decay 1e-4 → 1e-6 with 50 warmup steps.** Conservative.
  Higher peak LRs (3e-4) caused the model to forget Python-side priors
  it needed for some bench problems (e.g. file I/O patterns).
- **3 epochs.** Past 3 epochs validation loss plateaus and we see
  catastrophic interference patterns on `agc_pairs_naming` (the v13
  story — see experiments.md).

## 5. The full training command

```bash
python3 train/run_l3.py \
    --model mlx-community/Qwen2.5-Coder-7B-Instruct-4bit \
    --data train/dataset/agc_pairs_claude_dual.jsonl \
    --batch-size 2 --epochs 3 --max-seq 1024 --lora-rank 16 \
    --adapter-path train/lora_adapter_v12_l3
```

Output:
- `train/lora_adapter_v12_l3/adapters.safetensors` — final weights
- `train/lora_adapter_v12_l3/0000{100,200,...,1500}_adapters.safetensors` — checkpoint snapshots
- `train/lora_adapter_v12_l3/training_config.yaml` — the YAML mlx-lm consumed
- `train/lora_adapter_v12_l3/adapter_config.json` — adapter metadata

## 6. The evaluation cascade

Driver: `train/eval_checkpoint_cascade.py`.

The trick: load *one* trained adapter, then try the same problem at
*three checkpoints from that same training run*. For v12: `adapters`
(final, iter_1500), `0001000_adapters`, `0000500_adapters`.

Why this works: different overfit points produce different output
distributions. The final checkpoint is the most refined for typical
problems; intermediate checkpoints are less specialized and produce
more diverse samples on the few problems the final overfit past. The
cascade interleaves sample+verify per problem with **real early-stop
on first PASS** — no wasted token generation past the answer.

```bash
python3 train/eval_checkpoint_cascade.py \
    --model mlx-community/Qwen2.5-Coder-7B-Instruct-4bit \
    --base-adapter train/lora_adapter_v12_l3 \
    --checkpoints adapters,0001000_adapters,0000500_adapters \
    --prompt-format no_tests \
    --n 8 --temp 0.7 \
    --out bench/results/<date>/agc-v12-cascade.jsonl
```

### Server-side helpers (load-bearing)

The eval script does three things between sampling and verification:

1. **`build_prompt_no_tests`** (in `eval_bestof_n.py`). Builds the
   prompt with three elements the model needs to nail the right shape:
   - `extract_signature_hints(tests.ag)` — produces a `**REQUIRED**
     top-level function name(s):` directive listing the exact names
     the tests will invoke. Empirically essential — the model was
     renaming `letter_grade` → `grade` on its own before this directive.
   - `worked_example_hint(tests.ag)` — pastes the first test as a
     concrete example, so the model sees the exact arg-types it must
     accept.
   - `domain_hint(objective)` — small domain-keyword shaping (e.g.
     "round up" → emit `math.ceil`).
2. **`extract_defuns(text)`** (in `eval_bestof_n.py`). Pulls
   `(defun ...)` / `(extern ...)` / `(defstruct ...)` / `(def ...)`
   forms out of whatever the model emitted, even if it accidentally
   wrapped them in `(module …)` or trailed `(test …)` blocks.
3. **`assemble(defuns, tests_ag, required_funcs_from(tests_ag))`** —
   wraps the extracted defuns in a fresh `(module Submission ...)`,
   appends the canonical `tests.ag` content, and **injects passthrough
   wrappers** when the model defined a function with matching arity
   under the wrong name. For example, if tests call `letter_grade`
   but the model only defined `grade`, the assembler synthesizes:
   ```
   (defun letter_grade (a0 a1 a2 a3) (grade a0 a1 a2 a3))
   ```
   right before the `(test …)` block. This bridges the residual naming
   bias that survives the `REQUIRED-name` directive on the rare cand.

The assembled module then goes to
`Agentic.Cli check --allow-env --allow-file --allow-http --allow-db`,
which runs each `(test …)` against the reference interpreter and
reports `(ok (tests-passed N/N))` or an error diagnostic.

### Token accounting

Per problem, the cascade tracks `tokens_in` (prompt × candidates
sampled) and `tokens_out` (generation tokens, summed across cands).
Tok/pass is `(total_in + total_out) / pass_count`. The 30/30 @ 417
headline comes from this calculation against the v12 wrapped run.

**The early-stop is real.** Most problems pass on cands=1 at the head
checkpoint and never hit the second or third checkpoint at all.
30-grade-average is the typical hard case and uses n=4 sampling at
the head checkpoint.

## 7. Diagnostic tooling

`train/diag_24_tip_split.py` runs a single problem against a single
(model, adapter, checkpoint) combination at temp=0.7 with N
candidates, **dumping every raw generation, the assembled module,
and the verifier verdict** to JSONL. Used to diagnose the L7
regression on 24-tip-split (the v14 story); reusable for any future
per-problem investigation.

`train/gen_hard_pairs.py` reads a directory of subagent-produced JSON
files (`{topic, objective, solution}`), validates each solution via
`agc check`, drops failures, and merges survivors into a JSONL pair
file matching the canonical schema. Used to assemble the new
training-corpus buckets (`agc_pairs_hard.jsonl`,
`agc_pairs_capability.jsonl`).

`train/verify_batch.py` re-runs `agc check` over an existing pair
file end-to-end. Use this if anything in `Agentic.Check` changes
(e.g. semantics fixes) to confirm the corpus still validates.

## 8. Caveats and pitfalls

- **The `agc check` CLI is the source of truth.** `math.round` is
  registered in `Agentic.Check/ReferenceInterpreter.cs` but **not** in
  `Agentic.Core/Stdlib/MathModule.cs` — calling `math.round` from a
  user module produces "Function 'math.round' is not defined." The
  canonical 2-decimal rounding idiom across the bench is
  `(/ (math.floor (+ (* x 100) 0.5)) 100)`. Train data and bench
  references both use this form.
- **`math_ceil` (underscore) and `math.ceil` (dot) are equivalent**
  via the lexer's `_pureStdlibPrefixes` normalization in
  `Agentic.Core/Syntax/Lexer.cs`. Same for `str_*`, `arr_*`, `map_*`,
  `json_*`. Either form is fine in training data and at inference.
- **Token-count comparison across methods has a ~10-15% noise floor.**
  AGC uses `tokenizer.encode` counts; the Claude oneshot baseline uses
  `char/3.5` estimates. Don't quote sub-10% differences as significant.
- **The cascade evaluator vs the bestof_n evaluator.**
  `eval_bestof_n.py:sample_candidates` always generates **all N
  candidates before any verification** — early-stop only saves
  verifier wall time, not LLM token cost. `eval_checkpoint_cascade.py`
  interleaves sample+verify correctly. Prefer the cascade for any
  number that feeds the paper.
