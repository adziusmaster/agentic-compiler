# AgenticCompiler Benchmark Suite (D1)

Tier-B evaluation harness for the paper. Three tracks compared on the
same 30 natural-language objectives:

| Track             | Target output        | Retry policy                  | Verdict source          |
| ----------------- | -------------------- | ----------------------------- | ----------------------- |
| `agc`             | `.ag` source         | 5-attempt reflection loop     | `agc check` + tests.ag  |
| `python`          | `.py` + unit tests   | 5-attempt `pytest` feedback   | `pytest` exit code      |
| `python-oneshot`  | `.py` + unit tests   | single attempt (no retry)     | `pytest` exit code      |

## Problem layout

```
problems/NN-slug/
  objective.md      natural-language spec (single prompt — same text for all tracks)
  tests.ag          AgenticCompiler acceptance tests
  tests.py          pytest acceptance tests (semantically identical to tests.ag)
  meta.json         capability set, permission flags, mocks
```

Problems are indexed 01–30. First 10 are pure-logic (`pure/`), 11–20 are
capability-using (`cap/`), 21–30 are multi-helper decomposition
(`multi/`).

## Running

```
python3 run.py --track agc             # requires ANTHROPIC_API_KEY (or OPENAI/GEMINI)
python3 run.py --track python          # same
python3 run.py --track python-oneshot  # same
python3 run.py --track agc --only 01,02,03     # smoke test
python3 run.py --track agc --dry-run            # validate problem files only
```

Results land in `results/YYYY-MM-DD/<track>.jsonl`, one line per problem:

```json
{"id":"01-word-count","track":"agc","pass":true,"attempts":1,
 "wall_time_s":4.2,"tokens_in":432,"tokens_out":118,
 "source_loc":17,"capabilities":[],"decomposition_depth":1,
 "error_category":null}
```

## Metrics

- **pass** — all acceptance tests passed
- **attempts** — LLM calls before success (≥1; ≤5 retry, =1 oneshot)
- **wall_time_s** — end-to-end (prompt → verified)
- **tokens_in/out** — LLM API reports
- **source_loc** — non-blank lines of emitted source
- **capabilities** — declared capability set (AGC only; empty for Python)
- **decomposition_depth** — helper-function count (proxy for problem structure fit)
- **error_category** — on fail: `compile-error`, `test-fail`, `timeout`, `budget-exceeded`

## Reproducibility

Seed: `AGENTIC_BENCH_SEED` env var, default `42`. Models:
- AGC track: uses AGC's own LLM client (Anthropic/OpenAI/Gemini auto-detect)
- Python tracks: same provider+model, queried directly from `run.py`

To reproduce, run each track with the same `ANTHROPIC_MODEL` env (e.g.
`claude-sonnet-4-6`). Per-problem seeds are deterministic; tokens +
wall-time are reported but not controlled.
