# Agentic: a verifier-first language for LLM-authored code

Agentic (AGC) is a small Lisp-style language designed around one constraint:
**every program emitted by a language model must pass an independent formal
checker before it runs.** The compiler turns AGC source into native binaries;
the checker (`agc-check`, ≤1500 LOC, BCL-only) reads the binary plus a sidecar
manifest and decides accept/reject from first principles. Hallucination
becomes a build failure.

This repository contains the compiler, the verifier, a 30-problem benchmark,
the training corpus, and the LoRA adapters used to specialize local code
models (3B and 7B Qwen-Coder variants) on AGC. The current headline is a
single 7B-4bit adapter (v12) running cascade-style sampling across three
checkpoints of one training run; it produces 100% verified passes at
near-parity token cost with a frontier-LLM one-shot.


## Headline result (frozen 2026-06-01)

A **single 7B-4bit LoRA adapter (v12) with cascade-style sampling** solves all
30 bench problems with every solution verified by `agc-check`, at **near-parity
token cost with a frontier-LLM oneshot**:

| approach                                       | pass     | tokens/pass (avg) | verified? |
|------------------------------------------------|----------|-------------------|-----------|
| Claude Code subagent (Opus 4.7), one-shot      | 30/30    | 392               | no        |
| Local 7B v12 cascade, AGC                      | **30/30**| **417**           | **yes**   |
| Local 3B v6 single-shot, AGC *(2026-05-05)*    | 19/30    | 969               | yes       |
| Local 3B 5-adapter cascade, AGC *(2026-05-05)* | 30/30    | 3,209             | yes       |

Source: `bench/results/2026-05-07/agc-v12-l3-cascade-wrapped.jsonl` (30/30 @ 413)
and `bench/results/2026-05-07/agc-v12-l3-cascade-wrapped-run2.jsonl` (30/30 @ 420)
— two consecutive cascade runs at temp=0.7 (mean 417, variance 1.7%).
Claude baseline: `bench/results/2026-05-07/python-oneshot-claude.jsonl` with
provenance sidecar at `python-oneshot-claude.provenance.json`.

**Verification premium: 1.06×** — within the noise floor of the token-counting
methodology (`tokenizer.encode` for AGC vs `char/3.5` estimate for the Claude
baseline; ~10–15% method gap). 29 of 30 problems pass on the first greedy
sample; only 30-grade-average needs n=4 sampling.

**Compared against the prior 2026-05-05 result (3,209 tok/pass), this is an
8.2× → 1.06× drop in verification premium.** The reduction came from three
non-retraining changes — server-side wrapper-injection for entry-point name
mismatches, a stronger `REQUIRED-name` directive in the prompt, and a single
v12 adapter (rank 16, cosine LR, 7B base) replacing the 5-adapter 3B cascade.

**Status (2026-06-01):** four post-v12 LoRA experiments (v13–v16) failed to
beat v12 — each new training-data bucket caused catastrophic interference
elsewhere in the bench. v12 is the project headline; see
[`docs/experiments.md`](docs/experiments.md) for the full chronicle and
negative results.

> **Note on the Gemini Flash baseline.** Earlier headline tables compared AGC
> against Gemini Flash. The Gemini API is no longer accessible for this project
> (account inactive as of 2026-05-07); the Flash numbers in
> `bench/results/2026-04-23/` are preserved as historical reference. The live
> LLM baseline is now Claude Code Opus 4.7 at the equivalent of an isolated
> one-shot per problem.


## What this paper-track is and is not claiming

**It is claiming**:
- A small (7B-parameter, 4-bit-quantized, on-device) model fine-tuned on a
  human-curated 587-pair corpus can reach **100% pass on this benchmark** at
  near-parity token cost with a frontier-LLM one-shot, when paired with an
  independent formal checker. (The 2026-05-05 result reached 100% with a 3B
  base at higher token cost; the 7B v12 headline cuts that by ~8×.)
- The checker (`agc-check`) runs the reference operational semantics
  (`docs/semantics.md`) on every candidate. A passing run is a proof, not a
  vote. See `docs/soundness.md` for the formal soundness sketch.
- Capability use is statically declared (`@capability` on extern decls) and
  enforced both at compile-time and via the sidecar manifest. Undeclared I/O
  is a build error, not a runtime surprise.

**It is NOT claiming**:
- AGC syntax is more compact than Python/TS/C#. It isn't — see "syntax cost"
  below. AGC trades compactness for parser simplicity (so a small checker can
  re-derive semantics with no surprises).
- Sub-parity with the frontier LLM. We're at 1.06×, which we believe is
  within token-counting method noise; we don't claim AGC produces verified
  solutions in *fewer* tokens than a frontier LLM produces unverified ones.
- The local-7B story competes head-to-head on raw accuracy of arbitrary code
  generation. Bench problems are deliberately small (10–60 LOC) and
  contract-friendly. Larger ill-specified tasks are not in scope here.
- A single benchmark run is enough to claim near-parity. The 30/30 @ 417
  result was reproduced across two consecutive cascade runs at temp=0.7
  (413 and 420 tok/pass; mean ~417, variance 1.7%). It's robust against
  sampling noise but has not been validated on a held-out benchmark.


## Two distinct token costs

There are two questions worth disentangling.

**Q1: How verbose is the language at the syntax level?**
Hand-written reference solutions for 10 representative problems, tokenized
with `cl100k_base`:

| language    | total tokens | mean | median | ratio vs AGC |
|-------------|--------------|------|--------|--------------|
| AGC         | 1207         | 120  | 112    | 1.00×        |
| Python      | 615          | 61   | 57     | 0.51×        |
| TypeScript  | 712          | 71   | 74     | 0.59×        |
| C#          | 679          | 67   | 68     | 0.56×        |

Source: `bench/token_comparison.py`. AGC is **~2× more verbose** than Python
at the surface level — that's the cost of explicit type signatures and
S-expressions chosen for parser/checker simplicity.

**Q2: How many tokens does an LLM spend producing a *verified-correct*
solution?** The headline table above. As of 2026-05-07, the v12 cascade
matches a frontier-LLM one-shot to within ~6% (417 vs 392), close enough that
the comparison falls inside the noise floor of the token-counting methodology.
The verification premium has effectively closed at this benchmark scale.

For applications where a wrong-but-plausible answer is the failure mode
(safety-critical code, regulated industries, code paths gating money or PII),
this means a verifier-first pipeline no longer comes with a meaningful token
penalty — at least on small contract-driven problems.


## Architecture (one paragraph)

`Agentic.Core` is the untrusted compiler — it lexes, parses, type-and-capability
checks, and emits native binaries plus a JSON manifest. The manifest carries a
SHA-256 of the binary, the embedded source hash, the declared capability set,
and the embedded test/contract S-expressions. `Agentic.Check` is the trusted
verifier: it re-parses the manifest, re-runs the embedded test forms against
its own reference operational semantics, re-extracts capabilities from the
binary by string-scanning, and accepts iff all three guarantees hold:
**capability soundness** (syscalls ⊆ manifest), **test conformance** (every
embedded test reduces to pass), **contract validity** (`require`/`ensure`
clauses hold on the test inputs).

```
.ag source  ─►  Agentic.Core (untrusted)  ─►  binary  +  manifest.json
                                               │
                                               ▼
                          Agentic.Check (TCB, ≤1500 LOC, BCL-only)
                                               │
                                               ▼
                                       accept / reject
```

The TCB is auditable in 30 minutes (see `docs/tcb.md`). The whole thing rests
on three named axioms: TA-X (extractor soundness), TA-E (emitter faithfulness),
TA-H (SHA-256 collision resistance) — see `docs/soundness.md`.


## What's in this repo vs what you regenerate

**Tracked in git:** compiler/verifier source (`Agentic.*`), CLI, bench
problems + results, source training corpora
(`agc_pairs_{claude,naming,hard,capability,...}.jsonl`), training and
eval scripts, docs.

**Not tracked** (`gitignored`, regenerated locally — see
[`docs/training.md`](docs/training.md) §0): LoRA adapter weights
(~10 GB across versions), the augmented `*_dual.jsonl` training
files, the chat-format split (`train/mlx_chat/`), the extended
tokenizer + patched 4-bit base for the L7 experiment.

The v12 headline adapter rebuilds from a fresh clone in ~75 minutes
on an M-series Mac.


## Reproducing the result

The `train/` and `bench/` trees are reproducible end-to-end.

**1. Build the verifier and CLI.**
```bash
dotnet build AgenticLanguage.sln -c Debug
```

**2. Verify the curated 587-pair Claude-authored training corpus.** Each pair
is `agc check`-verified at curation time:
```bash
python3 train/verify_batch.py train/dataset/agc_pairs_claude.jsonl \
    --out /tmp/verify_smoke.jsonl
```

**3. Re-train the v12 adapter from scratch (≈75 min on 48 GB M-series,
≤12 GB peak).**
```bash
python3 train/run_l3.py \
    --model mlx-community/Qwen2.5-Coder-7B-Instruct-4bit \
    --data train/dataset/agc_pairs_claude_dual.jsonl \
    --batch-size 2 --epochs 3 --max-seq 1024 --lora-rank 16 \
    --adapter-path train/lora_adapter_v12_l3
```
This uses the no-tests dual corpus (each pair augmented to teach both the
"emit a full module with tests" and "emit defuns only" formats).
Server-side test re-attachment happens in `eval_checkpoint_cascade.py` via
`assemble(defuns, tests_ag, required_funcs_from(tests_ag))`.

**4. Run the v12 cascade against the 30-problem benchmark.**
```bash
python3 train/eval_checkpoint_cascade.py \
    --model mlx-community/Qwen2.5-Coder-7B-Instruct-4bit \
    --base-adapter train/lora_adapter_v12_l3 \
    --checkpoints adapters,0001000_adapters,0000500_adapters \
    --prompt-format no_tests \
    --n 8 --temp 0.7 \
    --out /tmp/agc-v12-cascade-replay.jsonl
```
The cascade interleaves sample+verify (real early-stop on first PASS)
across three checkpoints from the same v12 training run — final
(iter ~1500), iter_1000, iter_500. Most problems pass at cands=1 on the
final checkpoint; only the harder ones use the lower checkpoints' diversity.

The shipped results are at `bench/results/2026-05-07/agc-v12-l3-cascade-wrapped.jsonl` and `agc-v12-l3-cascade-wrapped-run2.jsonl` (the two reproducibility runs).


## Repository layout

```
Agentic.Core/         compiler (untrusted)
Agentic.Check/        independent verifier (TCB, ≤1500 LOC, BCL-only)
Agentic.Cli/          unified CLI: agc compile / check / verify / run
docs/                 formal foundations + methodology + experiments
  semantics.md        E1: small-step operational semantics
  effects.md          E2: type-and-capability-effect system
  soundness.md        E3: TH-Check + decomposition into TH-CS / TH-TC / TH-CV
  tcb.md              TCB inventory + 30-min re-audit checklist
  safety-policy.md    formal subject Π and the three guarantees
  training.md         training pipeline: corpus, hyperparameters, eval cascade
  experiments.md      version chronicle v4..v16 — what worked, what didn't, why
bench/
  problems/           30 benchmark problems (objective.md + tests.ag + tests.py)
  results/            historical eval data including baselines and cascade runs
  token_comparison.py syntax-cost comparison vs Python/TS/C#
train/
  dataset/            agc_pairs_claude.jsonl (587 verified, Claude-authored)
                      plus the older Gemini-distilled corpora used for v4/v5
  author_batch_v*.py  hand-authoring scripts that produced the corpus
  verify_batch.py     stage-jsonl → agc-check → append-if-passes
  finetune_mlx.py     base LoRA training driver
  run_l3.py           L3 variant driver: rank 16, cosine LR, batch 2, 3 epochs
  augment_no_tests.py corpus augmenter — pairs each example with a no-tests variant
  eval_checkpoint_cascade.py   sample+verify cascade across checkpoints of one run
  eval_bestof_n.py    single-adapter best-of-N comparator + server-side helpers
                      (extract_signature_hints, assemble, required_funcs_from)
  lora_adapter_v12_l3/           HEADLINE adapter (30/30 @ 417 cascade)
  lora_adapter_v11_7b_dual/      first 7B adapter, prior cascade member
  lora_adapter_v13_baseline/     v13 — naming-pair corpus, regressed (negative)
  lora_adapter_v14_tokenizer/    v14 — patched extended-vocab base (negative)
  lora_adapter_v15/              v15 — + 30 hard multi-step pairs (tied)
  lora_adapter_v16/              v16 — + 12 capability pairs (regressed)
  lora_adapter_v6..v10_claude/   historical 5-adapter 3B cascade (30/30 @ 3,209)
  lora_adapter_v4_3b/            historical 10/30 baseline (Gemini-distilled)
  lora_adapter_v5_hq/            historical 21/30 baseline (Gemini Flash-distilled)
  agc_tokenizer/                 L7 extended-vocab tokenizer (227 added tokens)
  diag_24_tip_split.py           per-problem cascade diagnostic
  gen_hard_pairs.py              subagent-output validator/merger
```


## Limitations and honest framing for the paper

- **Bench scale is small (30 problems, 10–60 LOC each).** Performance does
  not extrapolate to general code generation. Useful as a controlled
  contract-driven proof-of-concept.
- **Two of three guarantees rest on axioms, not proofs.** TA-E (emitter
  implements E1) and TA-X (extractor under-approximates the I/O footprint)
  are *named* and *bounded* but not mechanized. TC ("test conformance") is
  the strongest — it stands on the reference interpreter alone and does not
  invoke TA-E. See `docs/soundness.md` §6.
- **Token-cost parity is at the noise floor.** v12's 417 vs Claude's 392
  is a 6% gap, and the two numbers come from different counting methods
  (`tokenizer.encode` vs `char/3.5`). The 10–15% method gap means the
  comparison isn't tight enough to claim "matched cost"; we claim
  "indistinguishable from the noise floor at this benchmark scale."
- **The 417 result depends on two non-retraining helpers.** A
  `REQUIRED top-level function name(s)` directive in the prompt and a
  server-side `assemble()` that injects passthrough wrappers for entry-point
  name mismatches. Both are AGC-specific, both small, both honest — but
  worth naming as part of "what the system does."
- **Cascade is the same model at three checkpoints.** v12 final, iter_1000,
  iter_500 — different overfitting points give different output
  distributions, so the lower checkpoints contribute on the harder problems
  the final has overfit past. This is a free diversity trick but it does
  mean "cascade" here is a different beast from the 2026-05-05
  five-adapter cascade.
- **No mechanized proofs.** Soundness is pen-and-paper. A Coq/Lean port of
  E1+E2 is a clear follow-up.


## What we tried that didn't work

The lab notebook's negative results are part of the contribution. Full
chronicle in [`docs/experiments.md`](docs/experiments.md). Headline failures:

- **Custom tokenizer extension (L7, 2026-05-08).** Extended Qwen's
  151,665-token vocab with 227 AGC-specific tokens (stdlib calls, type
  annotations, s-expr openers) at IDs ≥ 151,665, with merge-init from the
  constituent old-token embeddings. Re-trained a v14 LoRA on the patched
  4-bit base. **Result: regression vs v12** (29/30 @ 850 tok/pass), with
  Y/Z token-count ratio averaging 0.998 (new tokens essentially unused at
  inference). Per-problem diagnosis (`train/diag_24_tip_split.py`) showed
  v14 emits the right stdlib name but mangled S-expr structure — Python-
  style infix `/` and unbalanced parens. The patched-base re-quantization
  noise corrupted structural priors. Artifacts at `train/agc_tokenizer/`,
  `~/.cache/agc/qwen-7b-extended-v1/`, `bench/results/2026-05-29/v1{2,3,4}-24-tip-split-cands.jsonl`.

- **Training-corpus expansion (v15 + 30 hard pairs, 2026-05-29).** Added
  30 verified multi-step financial problems targeting v12's
  token-burning hotspots. Ties v12 on pass-rate (30/30) at marginally
  worse tok/pass (428 vs 420), but every problem passes at the head
  checkpoint (no cascade fallthrough). The 23-rental-cost problem
  dropped from 1522 → 469 tokens — a real win — paid for by +600 each
  on 14-file-write-status and 20-file-copy. Local fix, global wash.
  `bench/results/2026-05-29/agc-v15-cascade.jsonl`.

- **Capability-aware corpus expansion (v16 + 12 capability pairs, 2026-06-01).**
  Added 12 file/env/http/db-using pairs to recover the v15 file-IO
  regressions. They did — but introduced new catastrophic interference:
  12-file-first-line collapsed (+5224 tok), 02-reverse-words +1004,
  16-env-join-two +1445. **Net: 30/30 @ 654 tok/pass, 1.67× premium —
  strictly worse than v15 and v12.** The corpus is small enough that
  each new training-data bucket reshuffles the model's distribution.
  `bench/results/2026-06-01/agc-v16-cascade.jsonl`.

- **Verifier-feedback agentic refinement (early 2026-05).** Tried a
  retry loop that fed `agc-check`'s failure messages back to the model
  asking it to fix specific failing assertions. The retry prompts were
  too long and lost the model into producing unparseable output (paren
  mismatches). Reverted in favor of pure resampling at temp=0.7.

- **Compact-prompt + temp=0 to push under 392.** Single experiment:
  shortened the prompt, set temp=0 on the first sample. Regressed to
  29/30 @ 924 — the compact prompt under-specified the format, and
  temp=0 returns the model's default-wrong answer on hard problems
  like 01-word-count. The verbose explicit-instruction prompt and
  all-temp=0.7 sampling are load-bearing. Reverted same day.


## License

Source-available; see `LICENSE`. Non-commercial research use is permitted.