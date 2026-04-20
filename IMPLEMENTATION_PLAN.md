# AgenticCompiler — Implementation Plan

Companion to `ROADMAP.md`. Strategy lives there; this document is the
week-by-week execution schedule, LOC budgets, and concrete exit criteria for
each stage. Update weekly with actuals.

## Ground rules

- **One stage in-flight per track.** Two tracks run in parallel (engineering
  + formalism). Never start a third.
- **Every stage has an exit test.** No stage is "done" without the exit test
  passing in CI.
- **The TCB budget is hard.** `Agentic.Check/` exceeding 1500 LOC blocks
  merge. Track weekly.
- **Formal docs land in `docs/` with the code they underwrite.** No doc PRs
  without corresponding code; no code PRs that break the doc.

## Tracks

Two parallel tracks for 14 weeks:

- **Track E (engineering)** — C / A stages. One engineer.
- **Track F (formalism)** — E stages. Can be the same person context-switched
  or a collaborator; either way, treat as an independent track.

```
Week  Track E (engineering)             Track F (formalism)
────  ─────────────────────────────    ───────────────────────────────
1     A5 hygiene → A4 cap breadth      E1 semantics (draft)
2     A4 cap breadth                   E1 semantics (freeze)
3     C5 safety policy doc + C6 hash   E2 effect system (draft)
4     C6 hash (finish) + C7 setup      E2 effect system (freeze)
5     C7 parser + ref interpreter      E3 soundness (skeleton)
6     C7 ref interpreter (finish)      E3 soundness (fill)
7     C7 capability extractor          E3 soundness (freeze)
8     C7 CLI + tests                   — (slack; catch up)
9     C8 VC emission                   — (slack; catch up)
10    C9 TCB audit + CI budget check   (optional) B2 spec
11    D1 bench scaffolding + 10 tasks  (optional) B2 impl
12    D1 bench 20 tasks + baseline     —
13    D2 auditability study            —
14    D3 paper draft                   —
```

## Stage-by-stage exit criteria

### Week 1 — A5 + A4 kickoff · E1 draft

**A5 exit.** `dotnet test` prints `Passed: N/N, Failed: 0`. No skipped tests.
One commit; the fix in `JsonModule` or `JsonModuleTests` with a one-line
rationale in `LanguageSpec.cs`.

**A4 kickoff.** `DefaultCapabilities.BuildTrusted()` has **all five** new
capabilities registered — `file.read`, `file.write`, `env.get`, `db.query`,
`process.spawn` — each with:

- `ParamTypes` / `ReturnType` from `AgType`
- `Permission` string
- `Adapter` that reads from an env-provided sandbox directory when
  `AllowRealIo`, returns a `NotSupportedException` otherwise
- `CSharpEmitExpr` that the transpiler emits verbatim

Tests: `CapabilityRegistryBreadthTests` covers permission-gate, unmocked
error, and mocked happy path per capability. Five samples (one per
capability) in `Agentic.Cli/samples/caps/`.

**E1 draft.** `docs/semantics.md` exists with rules for: numbers, strings,
booleans, arithmetic, comparisons, `if`, `while`, `def`, `set`, `defun`
(first-order), `defstruct`, `return`, function call, capability call (mocked
+ real), `assert-eq`, `assert-true`, `assert-near`, `require`, `ensure`,
`mocks`. Each rule uses the `E, σ → E', σ'` shape. Document is ≤ 15 pages.

### Week 2 — A4 finish · E1 freeze

**A4 exit.** Running `agc compile samples/caps/FileRead.ag --allow-file`
produces a binary that reads a sandboxed file; running without
`--allow-file` fails at compile time with `permission-denied` diagnostic.
Same for env / db / process / file-write.

**E1 exit.** Every construct used by `(test …)` bodies in existing samples
has a rule. Sketched review with a second pair of eyes (colleague or LLM).
File is committed and cross-referenced from `ROADMAP.md` Arc E.

### Week 3 — C5 safety policy + C6 binary hash · E2 draft

**C5 exit.** `docs/safety-policy.md` lists the three guarantees (capability
soundness, test conformance, contract validity) each as a predicate on
`(binary, source, manifest)`. Non-goals listed. README updated to distinguish
"capability manifest" from "proof-carrying" — the latter only appears where
the checker's guarantees apply.

**C6 exit.** `NativeEmitter.Emit` computes and embeds `BinaryHash`.
`agc verify <bin>` recomputes and checks; flipping one bit in the binary
makes it reject. New test `BinaryHashIntegrityTests` covers the
happy/tamper paths.

**E2 draft.** `docs/effects.md` exists with typing judgment
`Γ ⊢ e : τ ! Φ`, one rule per expression form in E1, and the
effect-monotonicity statement. Proof sketch per rule (paper-level, not
mechanized).

### Week 4 — C6 finish · C7 project setup · E2 freeze

**C6 finish.** Non-determinism check: compile the same source on two
machines, compare `BinaryHash`. Result documented in `docs/tcb.md` with the
chosen mitigation (per-emission binding vs. reproducible-build requirement).

**C7 setup.** New project `Agentic.Check/` with `Program.cs`, `Parser.cs`
stub, `ReferenceInterpreter.cs` stub, `CapabilityExtractor.cs` stub,
`Agentic.Check.csproj` referencing only BCL. CI: a budget-check script that
runs `cloc Agentic.Check/` and fails if output > 1500 non-blank non-comment
LOC. A target `Agentic.Check.Tests/` project exists with scaffolding.

**E2 exit.** Effect system frozen. A reviewer can read `docs/effects.md` in
45 min and follow the soundness argument. Cross-referenced from `tcb.md`.

### Weeks 5–6 — C7 parser + reference interpreter · E3 skeleton/fill

**C7.1 parser.** `Agentic.Check/Parser.cs` — a from-scratch S-expr parser
covering only the subset used by tests and contracts. Target: 200 LOC. It
does **not** share code with `Agentic.Core/Syntax/Parser.cs` — the whole
point is TCB isolation. Test: round-trip every manifest-embedded snippet in
the test suite.

**C7.2 reference interpreter.** `ReferenceInterpreter.cs` implements E1
rules literally. Target: 500 LOC. Every rule number in `docs/semantics.md`
is a comment tag on the corresponding code path. Tests: for each E1 rule,
a single-line `.ag` snippet that exercises it.

**E3 skeleton.** `docs/soundness.md` contains the theorem statement and the
three proof-obligation clauses. Each clause lists the lemmas it depends on
and where those lemmas live (`effects.md`, `semantics.md`, or "checker code
review"). No proofs yet — just the shape.

**E3 fill.** Each proof clause written out at paper-sketch level. Every
unproven step flagged as **trust-assumption** with a pointer to where it
will be closed (or explicitly deferred).

### Week 7 — C7 capability extractor · E3 freeze

**C7.3 capability extractor.** `CapabilityExtractor.cs` scans the emitted
binary for capability call-site signatures. Implementation choice: scan the
binary's embedded C# source (transpiler preserves it as a string resource)
*not* the native assembly — the C# source is deterministic and readable.
For each capability in `DefaultCapabilities`, match its `CSharpEmitExpr`
prefix against the embedded source. Output: `Set<string>` of invoked
capability names. Target: 200 LOC.

Tests: a `.ag` file that calls `http.fetch` and `file.read` → extractor
returns `{ "http.fetch", "file.read" }`. Tamper: remove a declared
capability from the manifest while keeping the binary → checker rejects.

**E3 exit.** `docs/soundness.md` frozen. Every trust-assumption is listed
in `docs/tcb.md` (Arc C9 below uses this list).

### Week 8 — C7 CLI + integration tests

**C7.4 CLI.** `Agentic.Check/Program.cs` wires the pieces:

```
agc-check <binary> [--source <file.ag>] [--policy safety|strict]
```

Exit codes:
- 0 — all checks pass
- 10 — source-hash mismatch
- 11 — binary-hash mismatch
- 20 — undeclared capability in binary
- 30 — test failed in reference semantics
- 31 — contract violated
- 40 — manifest malformed or missing

Each non-zero exit prints a structured diagnostic to stderr. Happy path
prints a JSON summary to stdout (report: source hash, binary hash,
capabilities, tests passed, contracts satisfied).

**C7 exit.** Five negative tests (source tamper, binary tamper, missing
cap, failing test, violated contract) + happy path. All green. Checker LOC
budget (≤ 1500) holds.

### Week 9 — C8 VC emission

**C8 exit.** `ProofManifest` extended with structured (JSON) test bodies
and contract bodies. Transpiler emits them. Reference interpreter parses
and evaluates the JSON form (not the `(mocks …)` S-expr string). Running
`agc-check <bin>` without `--source` still validates tests and contracts.
A new test `WithoutSourceCheckerTests` covers this path.

### Week 10 — C9 TCB audit + B2 (optional)

**C9 exit.** `docs/tcb.md`:

- LOC count per file in `Agentic.Check/` (from `cloc`).
- Dependency list: `System.Text.Json`, `System.Security.Cryptography`,
  `System.IO.File`. Nothing else.
- Axioms: (a) emitter faithfully implements E1 subset, (b) extractor
  pattern set covers `DefaultCapabilities`, (c) SHA256 collision
  resistance.
- Attack surface: documented adversary model (malicious compiler,
  malicious binary, malicious manifest, malicious source) and what each
  can and cannot achieve.
- CI: `.github/workflows/tcb-budget.yml` (or equivalent) runs cloc and
  fails if budget exceeded.

**B2 (optional).** If time permits and D1 is not yet started, scope the
autonomous Planner. Otherwise defer to the follow-up paper and update the
roadmap.

### Weeks 11–12 — D1 benchmark suite

**D1.1 scaffolding.** `bench/` directory with a `run.py` (or shell script)
that takes a provider + API key, runs all 30 problems, records per-problem
metrics to `bench/results/<date>/results.jsonl`. Each problem lives in
`bench/problems/NN-name/` with `objective.md`, `tests.ag`, and optionally
a `functions.ag` for constrained problems.

**D1.2 problems.** 30 problems authored. Coverage:

- 10 pure-logic (parsing, transformation, data-structure ops).
- 10 capability-using (http, file, db, env, process — two each).
- 10 multi-helper decomposition (5–15 helpers each).

**D1.3 baseline.** `bench/python_baseline/` — same 30 objectives, expects
LLM to write Python + pytest; harness runs `pytest` as the retry oracle.
Same metrics as AgenticCompiler run.

**D1 exit.** One run of both tracks completes. Results committed. No
crashes outside the scheduled 3-attempt reflection budget.

### Week 13 — D2 auditability study

**D2 exit.** Study protocol + results in `bench/audit_study/`. 10 problems
× 2 forms (`.ag` vs Python) × 3 reviewers = 60 audit tasks. Each task:
one seeded bug (from 3 classes). Time-to-audit and audit-correctness
recorded. Results written up in `docs/audit-study-results.md`.

### Week 14 — D3 paper draft

**D3 exit.** `paper/draft.tex` exists with sections:

1. Motivation
2. Language + Type-and-Effect System (from E2)
3. Capability FFI + Manifest (from C1–C4)
4. The Checker: agc-check (from C5–C9, tcb.md)
5. Evaluation (from D1, D2)
6. Related Work
7. Limitations + Future Work (mechanized emitter, full PCC, HOF effects)

Target venue decided: PLDI / OOPSLA / ICSE / workshop — based on how
tight E3 and C7 turned out.

## LOC budgets and watch-list

| Component              | LOC budget    | Rationale                                    |
|------------------------|---------------|----------------------------------------------|
| `Agentic.Check/`       | ≤ 1500        | Minimal TCB; reviewers read the whole thing. |
| `Agentic.Check.Tests/` | no budget     | More tests = better.                         |
| `docs/semantics.md`    | ≤ 20 pages    | Paper-level formalism.                       |
| `docs/effects.md`      | ≤ 15 pages    | Typing rules + soundness sketch.             |
| `docs/soundness.md`    | ≤ 10 pages    | Theorem + proof obligations + assumptions.   |
| `docs/tcb.md`          | ≤ 5 pages     | Reviewer-facing, scannable.                  |
| `docs/safety-policy.md`| ≤ 3 pages     | One-page policy + non-goals.                 |

## Risks and mitigations

1. **Checker scope creep.** Mitigation: weekly `cloc` budget check in CI.
   Refusing to merge PRs that breach it forces discipline.
2. **Non-deterministic emission.** Measure in week 4. If `dotnet publish`
   produces bit-different binaries, document the gap and either (a)
   require reproducible builds in CI, or (b) relax `BinaryHash` to a
   per-emission anchor and lean on `SourceHash` for cross-machine claims.
3. **Formalism drift.** Mitigation: every reference-interpreter code path
   has a `// E1-rule-N` comment matching `docs/semantics.md`. A doc PR
   that changes a rule must touch the matching code path.
4. **Bench authorship bottleneck.** 30 problems × ~30 min each = 15 hours
   of authorship. Start problem authorship in week 9 background instead
   of week 11 foreground if other tracks have slack.
5. **Single-pair-of-eyes soundness.** At week 7 (E3 freeze), send
   `docs/soundness.md` to a PL-literate reviewer for a sanity check
   before C9. A second opinion here is cheap insurance.

## Tracking

Update this file weekly. Format: add a `## Week N actuals` section under
"Ground rules". Record: what shipped, what slipped, LOC budget status,
any assumptions rewritten.
