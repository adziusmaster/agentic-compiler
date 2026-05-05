# Agentic: a verifier-first language for LLM-authored code

Agentic (AGC) is a small Lisp-style language designed around one constraint:
**every program emitted by a language model must pass an independent formal
checker before it runs.** The compiler turns AGC source into native binaries;
the checker (`agc-check`, ≤1500 LOC, BCL-only) reads the binary plus a sidecar
manifest and decides accept/reject from first principles. Hallucination
becomes a build failure.

This repository contains the compiler, the verifier, a 30-problem benchmark,
the training corpus and adapters used to specialize a local 3B model on AGC,
and the cascade that produces 100% verified passes.


## Headline result (2026-05-05)

A **5-adapter LoRA cascade** over `Qwen2.5-Coder-3B-Instruct-4bit` solves all
30 bench problems with every solution verified by `agc-check`:

| approach                              | pass     | tokens/pass (avg) | verified? |
|---------------------------------------|----------|-------------------|-----------|
| Gemini Flash, Python one-shot         | 28/30    | 678               | no        |
| Gemini Flash, Python retry            | 30/30    | 641               | no        |
| Local 3B v6 single-shot, AGC          | 19/30    | **969**           | yes       |
| **Local cascade v6→v7→v8→v9→v10, AGC**| **30/30**| **3,209**         | **yes**   |

Source: `bench/results/2026-04-23/*.jsonl`, `bench/results/2026-05-05/agc-cascade-final.jsonl`.

The cascade tries each adapter greedy in turn (cheapest first) and only escalates
to sampling on verifier failure. **23 of 30 problems pass on the first greedy
sample at median 550 tokens** — the remaining 7 consume diversity-sampling budget.


## What this paper-track is and is not claiming

**It is claiming**:
- A small (3B-parameter, 4-bit-quantized, on-device) model fine-tuned on a
  human-curated 587-pair corpus can reach **100% pass on this benchmark**
  when paired with an independent formal checker and a multi-adapter cascade.
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
- Our cascade is cheaper per token than Gemini Flash. It isn't — Flash with
  Python retry hits 30/30 at ~641 tokens/pass vs our 3,209. The gap is the
  cost of verification. We pay ~5× more tokens for "every output is provably
  correct against the spec".
- The local-3B story competes head-to-head on raw accuracy of arbitrary code
  generation. Bench problems are deliberately small (10–60 LOC) and
  contract-friendly. Larger ill-specified tasks are not in scope here.


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
solution?** The headline table above. Flash wins on raw cost; the cascade
wins on the verification guarantee.

For applications where a wrong-but-plausible answer is the failure mode
(safety-critical code, regulated industries, code paths gating money or PII),
the verification premium is the point. For everyday glue code, Flash with
retry is more cost-effective.


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

**3. Re-train any adapter from scratch (≈17 min on M-series, ≤12 GB peak).**
```bash
python3 train/finetune_mlx.py \
    --data train/dataset/agc_pairs_claude.jsonl \
    --adapter-path train/lora_adapter_v10_claude \
    --batch-size 4 --epochs 3 --max-seq-length 1024
```

**4. Run the 5-adapter cascade against the 30-problem benchmark.**
```bash
python3 train/eval_cascade.py \
    --adapters train/lora_adapter_v6_claude \
               train/lora_adapter_v7_claude \
               train/lora_adapter_v8_claude \
               train/lora_adapter_v9_claude \
               train/lora_adapter_v10_claude \
    --sample-n-per-adapter 8 --sample-temp 0.8 --max-tokens 1200 \
    --out /tmp/agc-cascade-replay.jsonl
```

The shipped result is at `bench/results/2026-05-05/agc-cascade-final.jsonl`.


## Repository layout

```
Agentic.Core/         compiler (untrusted)
Agentic.Check/        independent verifier (TCB, ≤1500 LOC, BCL-only)
Agentic.Cli/          unified CLI: agc compile / check / verify / run
docs/                 formal foundations
  semantics.md        E1: small-step operational semantics
  effects.md          E2: type-and-capability-effect system
  soundness.md        E3: TH-Check + decomposition into TH-CS / TH-TC / TH-CV
  tcb.md              TCB inventory + 30-min re-audit checklist
  safety-policy.md    formal subject Π and the three guarantees
bench/
  problems/           30 benchmark problems (objective.md + tests.ag + tests.py)
  results/            historical eval data including baselines and cascade runs
  token_comparison.py syntax-cost comparison vs Python/TS/C#
train/
  dataset/            agc_pairs_claude.jsonl (587 verified, Claude-authored)
                      plus the older Gemini-distilled corpora used for v4/v5
  author_batch_v*.py  hand-authoring scripts that produced the corpus
  verify_batch.py     stage-jsonl → agc-check → append-if-passes
  finetune_mlx.py     LoRA training driver
  eval_cascade.py     multi-adapter cascading inference
  eval_bestof_n.py    single-adapter best-of-N comparator
  lora_adapter_v6..v10_claude/   the 5 cascade adapters
  lora_adapter_v4_3b/            historical 10/30 baseline (Gemini-distilled)
  lora_adapter_v5_hq/            historical 21/30 baseline (Gemini Flash-distilled)
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
- **Token cost is 5× Flash.** Flash + Python retry hits 30/30 at lower
  per-pass cost. The verification guarantee is what justifies the premium;
  if you don't need it, you don't need this.
- **Cascade is curated.** v6–v10 are five differently-overfit checkpoints of
  the same base; the cascade ordering was chosen by hand using the per-problem
  pass tables. We have not shown that this generalizes to a fresh benchmark.
  Cross-validation on a held-out problem set is open work.
- **No mechanized proofs.** Soundness is pen-and-paper. A Coq/Lean port of
  E1+E2 is a clear follow-up.


## License

Source-available; see `LICENSE`. Non-commercial research use is permitted.
