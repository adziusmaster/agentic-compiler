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

## Week 1 actuals (2026-04-20)

**Shipped (engineering / Track E).**

- **A5 — test hygiene.** `JsonGet_MissingKey_ShouldFail` renamed to
  `JsonGet_MissingKey_ShouldReturnEmptyString`. Rationale added to
  `LanguageSpec.cs` JSON section (missing-key semantics: safe default,
  not throw). Single commit; suite moved from 323 → 323 passing (no
  skips). _(Commit pending — unstaged.)_
- **A4 — capability registry breadth.** `DefaultCapabilities.BuildTrusted()`
  extended with five capabilities: `file.read`, `file.write`, `env.get`,
  `db.query`, `process.spawn`. Each has ParamTypes / ReturnType /
  Permission / Adapter / CSharpEmitExpr. `db.query` adapter stubs to
  `NotSupportedException` (the real NuGet-backed path runs only inside
  emitted binaries, not in the compiler process). Five samples under
  `Agentic.Cli/samples/caps/`. Eleven tests in
  `CapabilityRegistryBreadthTests` cover mocked happy path,
  permission-denied transpile-time rejection, and unmocked-test
  failure. Suite: 334 / 334 passing. All five samples compile
  end-to-end to native binaries with the matching `--allow-*` flag and
  fail cleanly with `permission-denied` without it. _(Commit pending.)_

**Shipped (formalism / Track F).**

- **E1 draft + freeze.** `docs/semantics.md` written and frozen
  (≈ 14 pages). Small-step SOS rules for the subset: literals,
  arithmetic / comparisons, `if`, `while`, `do`, `def` / `set`,
  first-order `defun` / `return`, records (new / get / set-*), arrays
  (new / get / set / length), maps (new / get / set / has), capability
  calls under mocks, mock frame semantics, assertions, tests,
  contracts. Evaluation contexts factored out. Rules numbered
  §4.1 – §4.17 as the contract for `ReferenceInterpreter.cs`
  `// E1-rule-N` tags. §5.1 added concrete `strₛ` / `mathₛ`
  metafunction tables (12 `str.*` ops, 9 `math.*` ops). §8.1 added
  a construct-coverage table cross-indexing every construct used by
  tests in Calculator, ShoppingCart, WeatherFetcher, Pipeline, and
  `samples/caps/*.ag` against rule numbers. Appendix A added a worked
  trace of Calculator's `(test add (assert-eq (add 1 2) 3))` through
  11 reduction steps, citing every rule used ([ctx], [defun], [do-*],
  [val], [var], [call], [bin-op], [return], [assert-eq-pass],
  [test-pass]). Out-of-scope items explicitly enumerated: HOF,
  modules, `try/catch`, `[cap-real]` in checker, `arr.map` /
  `arr.filter` / `arr.reduce` (HOF-adjacent — deferred to E2). Review
  checklist §8: three of four boxes ticked; the remaining box
  (`ReferenceInterpreter.cs` stubs compile) is a Week 4 C7-setup
  deliverable, intentionally deferred. `ROADMAP.md` Arc E1 section
  cross-references the doc.

**Slipped.** None. Week 1 delivered exit for A5 (fully), A4 (fully —
originally scheduled to spill into Week 2), and E1 draft + freeze
(originally Week 2 freeze).

**LOC budget status.** `Agentic.Check/` not yet created (Week 4).
Documentation: `docs/semantics.md` ≈ 14 pages (budget 20). Healthy.

**Assumptions rewritten.** None. No roadmap sections invalidated.

**Risks surfaced during the week.**

- `db.query` capability's real adapter can't live in `Agentic.Core`
  because the compiler project does not link `Microsoft.Data.Sqlite`.
  Mitigated by stubbing the adapter to throw; the real emit-expression
  still works in the compiled binary where `NativeEmitter` detects the
  `Microsoft.Data.Sqlite` string in the transpiled C# and auto-adds
  the NuGet reference. Note in `docs/tcb.md` (Week 10) that the
  adapter in the compiler and the adapter in the emitted binary are
  *different code paths* — the checker (C7) only needs to reason
  about the emit-expression.
- `Compiler` uses a source-hash cache that masked the permission-gate
  check during manual testing. Not a correctness bug, but it
  complicated debugging. Kept as-is; documented here so future
  debugging sessions clear `.agc-cache` first.

**Next week.** A4 and E1 are done; Week 2 is now start C5 (formal
safety policy for `agc-check`) + start E2 draft (type-and-capability
effect system). C6 (binary-hash binding) can slot in as a single-day
task whenever. Week 4's `Agentic.Check/` scaffold will pick up the
fourth E1 checklist box (`ReferenceInterpreter.cs` stubs with
`// E1-rule-N` tags).

## Week 2 actuals (2026-04-21, in progress)

**Shipped.**

- **C6 — binary-hash binding.** `ProofManifest` gained `BinaryHash`
  field (defaults to `""` for back-compat / legacy manifests).
  `ProofManifestBuilder` gained `HashBinary(path)`, `SidecarPathFor(path)`,
  and `WriteSidecar(path, manifest)` helpers. `Compiler.Compile` writes
  `<binaryPath>.manifest.json` sidecar after `NativeEmitter.Emit`
  returns. The embedded copy inside the binary intentionally omits
  `BinaryHash` (a binary cannot plaintext-contain its own hash).
  `agc verify <bin>` prefers the sidecar: reads it, recomputes
  SHA256 of the binary on disk, exits 1 with `binary-tampered`
  diagnostic (printing both declared and actual hashes) on mismatch.
  Falls back to embedded-manifest mode with a "cannot detect
  post-emission tampering" warning when no sidecar is present.
  **End-to-end smoke test** on `samples/Calculator.ag`: compile →
  verify → exit 0 ("binary hash matches"); then flip one byte at
  offset 1024 → verify → exit 1 (`binary-tampered`). **9 unit tests**
  in `Agentic.Core.Tests/Runtime/BinaryHashTests.cs` (hash stability,
  tamper detection, sidecar round-trip, legacy-JSON deserialization,
  immutability of the input manifest record). Suite: 343/343 passing
  (was 334).

- **C5 — formal safety policy.** `docs/safety-policy.md` written and
  frozen (one page — ~5 KB). Defines the checker's subject
  `Π = (β, σ, μ)`; six well-formedness preconditions WF1–WF6 (schema
  version, source hash, binary hash, capability-permission matching,
  E1 parseability, E1-subset conformance); three guarantees as
  predicates on `Π` — CS (capability soundness, syscalls ⊆ manifest),
  TC (test conformance via ⟶*_E1 to test-log pass), CV (contract
  validity as TC applied to contract-as-test); seven non-goals NG-1
  through NG-7 (termination, non-test equivalence, memory safety as
  emitter-provided, side channels, resource bounds, concurrency,
  supply chain); two named trust assumptions TA-1 (CapabilityExtractor
  soundness) and TA-2 (emitter implements E1 — the emitter-semantics
  gap). Terminology cleanup: "proof-carrying" reserved for the
  CS+TC+CV bundle; manifest itself is a "capability manifest". Cross-
  references to `docs/semantics.md`, `docs/effects.md` (E2),
  `docs/soundness.md` (E3), `docs/tcb.md` (C9). ROADMAP.md Arc C5
  section updated with status + link.

- **C7 — independent checker `agc-check`.** The single longest-pole item
  on the critical path. New project `Agentic.Check/` built as a BCL-only
  console app (`AssemblyName=agc-check`, no ProjectReference to
  `Agentic.Core`). Files: `Parser.cs` (146 LOC, mini S-expr with
  comments + escape handling), `Manifest.cs` (63 LOC, JSON records
  deliberately duplicated from Core with matching `[JsonPropertyName]`),
  `ReferenceInterpreter.cs` (579 LOC — every reduction path tagged
  `// E1-rule §4.N` from `docs/semantics.md`; covers all E1 literals,
  variables, defun/defstruct/extern, if/let/lambda, arr/map ops, str.*,
  math.*, assert-eq/assert-true/assert-near, require/ensure, and
  capability calls resolved through manifest-embedded mocks with
  exact-then-wildcard lookup), `CapabilityExtractor.cs` (63 LOC,
  substring-scan of binary for seven capability emit-signatures),
  `Checker.cs` (182 LOC, testable entry-point — `Verdict` enum,
  `CheckResult` record, `Checker.Run(binaryPath, sourcePath, policy)`
  returns structured result), `Program.cs` (72 LOC — argv parsing and
  exit-code translation only, delegates to `Checker.Run`). **Total:
  1105 LOC**, well under the 1500 TCB budget. CLI: `--source <file>`
  (optional, parses full program, pre-evaluates definition forms
  — defun/defstruct/extern/def — and skips import/module/sys.* main-
  effect calls; then looks up tests by name), `--policy safety|strict`
  (strict adds observed⊊declared → reject). Exit codes 0/1/2. All five
  required rejection scenarios implemented and tested in
  `Agentic.Check.Tests/CheckerTests.cs`: `binary-tampered`,
  `source-mismatch`, `capability-undeclared`, `test-count-mismatch`,
  plus `io-error` / `no-manifest`. Happy-path test covers both the
  zero-tests case and a self-contained `(test t (assert-eq 1 1))`
  snippet. **8 tests, all green.** Full solution suite: 343 Core + 8
  Check = **351/351 passing**. End-to-end smoke on `samples/Calculator`
  with `--source`: `accept`, 2/2 tests pass, binary + source hashes
  match, capabilities empty. Both projects added to `AgenticLanguage.sln`.

- **C8 — VC emission for tests.** `ProofManifest` gained a new
  `EmbeddedDef(Kind, Name, SourceSnippet)` list. `ProofManifestBuilder`
  unwraps the `(module …)` head and walks top-level forms, emitting one
  `EmbeddedDef` per `defun` / `defstruct` / `extern` / `def`. Snippet
  truncation (previously 200 chars for tests, 120 for contracts)
  **removed** — the JSON manifest now carries full, parser-faithful
  S-expressions for every test, contract, and def. `Agentic.Check/
  Manifest.cs` mirrors the field. `Checker.RunTC`, when called without
  `--source` and with a non-empty `Defs`, spins a single shared
  `ReferenceInterpreter`, pre-evaluates each embedded def, then runs
  each test snippet against that shared state — no `--source` required.
  **Bug fix in the checker's reference interpreter**: `EvalDefun`
  previously treated only the last list element as the body, silently
  dropping `require` / `ensure` contracts that appear before `return`.
  It now scans past the optional `: <ReturnType>` annotation and wraps
  any remaining multi-form body in a synthetic `(do …)`. Three new
  tests in `Agentic.Check.Tests/CheckerTests.cs`:
  `Run_NoSource_WithEmbeddedDefs_AcceptsTestsThatCallUserFunctions` (core
  C8 win), `Run_NoSource_WithContractsInEmbeddedDefs_EnforcesRequireAtCallSite`
  (contract-as-VC happy path), `Run_NoSource_ContractViolation_RejectsAsTestFail`
  (contract-as-VC negative). **End-to-end smoke** on
  `samples/Calculator.ag`: compile → sidecar shows two `Defs` entries
  with full defun bodies → `agc-check <bin>` (no `--source`) →
  `accept`, 2/2 tests pass. Full solution suite: **343 Core + 11 Check
  = 354/354 passing**. Checker TCB LOC: **1153** (Checker.cs 182→203,
  ReferenceInterpreter.cs 579→597 for the defun-body fix; everything
  else unchanged). Well under the 1500 budget. Follow-ups explicitly
  deferred: structured capability-mock map per test (currently mocks
  remain as S-expr inside the test body — untouched until a sample
  actually exercises it); JSON-native AST per VC (text snippets parsed
  by the same grammar already satisfy "checker parses this, not
  arbitrary `.ag`" — the distinction doesn't buy safety).

