# AgenticCompiler — Foundation-to-Paper Roadmap

This document is the implementation plan that takes AgenticCompiler from its
current proof-of-concept state to a system with a defensible paper-worthy
thesis. It supersedes the in-flight `Stage 1 / 2 / 3` plan from
`~/.claude/plans/awesome-glad-you-like-glittery-oasis.md` where they conflict.

## Thesis

The claim worth defending:

> A compiler can isolate LLM-authored code to an audit-sized, capability-gated
> DSL while preserving access to the host ecosystem through typed contracts.
> The resulting native binary carries its own tests, contracts, and capability
> manifest, so its behavior can be re-verified from the artifact alone — no
> trust in the generation process required.

This reframes the project away from competing with Cursor/AlphaCodium/Devin on
task pass-rate, and toward a niche that is genuinely underexplored:
**proof-carrying LLM code**. Success criterion: a human auditor can certify a
200-line `.ag` program faster than the equivalent Python, and the capability
manifest guarantees no surprise I/O.

### Candidate paper framings

- "Proof-Carrying LLM Code: A Compiler for Auditable AI-Authored Programs"
- "Capability-Gated LLM Synthesis: Isolating AI Logic Behind a Typed FFI"
- "Small-Surface DSLs as a Substrate for Trustworthy LLM Code Generation"

Pick a framing once the evaluation (Arc D) results are in; do not pre-commit.

## Structure

Four arcs. A–C are the technical contribution; D is the paper. A and C are on
the critical path. B strengthens the claims. D is mandatory.

Each stage lists:

- **Objective** — one sentence.
- **Deliverables** — concrete artifacts (files, tests, samples).
- **Acceptance** — how you know it is done.
- **Paper hook** — what claim this stage supports.

## Arc A — Unblock decomposition (remove scaling walls)

The current ceiling is imposed by transpiler and probe-mechanism limitations,
not by the architecture. Fix these first; everything downstream compounds.

### A1 — Canonical value serialization for micro-test probes

**Objective.** Make `(sys.stdout.write <value>)` produce a canonical,
round-trippable text form for every `AgType`, so the existing Implementer
probe works for non-numeric helpers.

**Deliverables.**

- `Agentic.Core/Runtime/CanonicalFormat.cs` — serializer and deserializer for
  `double`, `string`, `bool`, `Array<T>`, `Map<K,V>`, `Record`, and nested
  combinations.
- Rules (deterministic, round-trippable):
  - `double`: shortest round-trip form; `-0` normalized; `NaN` / `Inf`
    spelled out.
  - `string`: JSON-style escaping, always double-quoted.
  - `bool`: `true` / `false`.
  - array: `[a, b, c]` with canonical elements.
  - record: `TypeName{field1: v1, field2: v2}` in declaration order.
- Update `sys.stdout.write` in `StdlibModules.cs` to emit canonical form.
- Update `ConstraintParser` to parse the canonical form for `expect:` values.

**Acceptance.** Implementer can synthesize and verify helpers returning
strings, arrays, records, and nested records. New sample: `ShoppingCart.ag`
with helper
`(defun total-by-category ((items : (Array Item))) : (Map Str Num) …)`
decomposed from a bare objective.

**Paper hook.** Decomposition is general — micro-tests are not numeric-only.

### A2 — Full type plumbing in the Transpiler

**Objective.** `defun` parameters and return types use the real `AgType`, not
hard-coded `double`.

**Deliverables.**

- `Transpiler.EmitFuncSignature` reads from `TypeInferencePass.GetFuncType`
  (already populated).
- Extend `Agentic.Core/Execution/Transpiler.cs` to emit `string`, `bool`,
  records, arrays, and maps as parameter and return types.
- Keep `Verifier` in sync — it already handles mixed types at runtime.
- Backfill tests: one transpiler snapshot per type crossing a function
  boundary.

**Acceptance.** `Distance.ag` and `Rectangle.ag` refactored so `Point` is
passed by value into helper functions. New sample: `PointOps.ag` with
`(defun midpoint ((a : Point) (b : Point)) : Point …)`.

**Paper hook.** The DSL is not toy-grade; real record and array composition
crosses function boundaries.

### A3 — Higher-order functions as first-class values

**Objective.** `(defun)` values are first-class; `arr.map` / `arr.filter` /
`arr.reduce` accept LLM-written helpers as arguments.

**Deliverables.**

- `FuncType` becomes a runtime value (`AgFunc` record holding a delegate or
  AST reference).
- Verifier: treat function references as values; dispatch through them in
  `arr.map` et al.
- Transpiler: emit `Func<…>` for function-typed params; generate lambdas or
  method-group references at call sites.
- Tests: `arr.map` / `arr.filter` / `arr.reduce` with LLM-written predicates
  and mappers.
- Sample: `Pipeline.ag` composing `arr.map → arr.filter → arr.reduce`.

**Acceptance.** Implementer generates and verifies a helper that takes or
returns a function.

**Paper hook.** The DSL supports compositional idioms LLMs naturally produce.

## Arc B — Hierarchical decomposition and iterative refinement

Makes the Planner real. Without Arc A, this stalls; with A, this is where the
architecture earns its framing.

### B1 — Reflection loop (replace 3-shot retry)

**Objective.** Replace the 3-attempt hard cap with a budgeted
iterative-refinement loop driven by LLM-authored diagnosis.

**Deliverables.**

- `Agentic.Core/Agent/ReflectionLoop.cs` — on a failed `FeedbackEnvelope`,
  calls the LLM with a *diagnosis* prompt ("which helper is wrong, why,
  suggest a fix approach") before the next generation attempt.
- Best-so-far tracker in `Orchestrator`: track highest passing-test count
  across attempts; reject regressions.
- Budget shape: `IterationBudget { maxAttempts, maxTokens, maxWallClock }`.
  Whichever binds first ends the loop.
- Tests: mock `IAgentClient` returns buggy-then-fixed programs; assert the
  loop terminates on the first all-green attempt and rejects regressions.

**Acceptance.** A contrived multi-bug sample the 3-shot loop cannot solve
but the reflection loop solves within budget.

**Paper hook.** Structured diagnosis beats blind retry; provides the
"how much does reflection add?" ablation.

### B2 — LLM-driven Planner (autonomous decomposition)

**Objective.** From a bare objective (no `functions:` block), the Planner
asks the LLM for a decomposition into helpers with signatures, intents, and
micro-tests, then hands the result to the existing Implementer / Composer.

**Deliverables.**

- `Agentic.Core/Agent/Planner.cs` — takes an objective string and top-level
  `(test …)` blocks; returns a `Plan { mainObjective, subFunctions[] }`.
- Plan format: structured S-expression output parsed by `ConstraintParser`.
- **Plan evaluator** (the hard part, not the prompt):
  - *Dry-run synthesis*: given only the proposed signatures, ask the LLM to
    sketch a main-body pseudo-composition. If it cannot, the plan is missing
    a helper — re-plan.
  - *Coverage check*: for each top-level test, which helper-chain does the
    LLM claim implements it? Reject plans with orphan tests.
- Tests: golden-plan snapshots for 3 representative objectives; rejection
  tests for deliberately-missing-helper plans.

**Acceptance.** `dotnet run -- agent "build a todo list with categories that
computes per-category totals"` produces a correct multi-helper program with
no author-declared `functions:`.

**Paper hook.** Autonomous decomposition is the central claim. Without
this, the paper is just TDD-with-retry.

### B3 — Hierarchical / module-level decomposition

**Objective.** Sub-plans own sub-helpers; Planner is recursive; large
programs decompose at module boundaries.

**Deliverables.**

- Planner produces a tree, not a flat list. Each sub-plan has its own
  objective + sub-helpers + sub-tests.
- Composer operates recursively: leaves first (Implementer), inner nodes
  composed from children.
- Cross-module: Planner can emit a sub-module (new `.ag` file with its own
  exports) when a sub-plan exceeds a helper-count threshold (e.g. > 5
  helpers). Uses the existing multi-file import system.
- Tests: a deliberately-complex objective (e.g. "simulate a vending machine
  with coin handling, inventory, and transaction logs") decomposes into ~3
  modules and ~12 helpers total.

**Acceptance.** The vending-machine-scale sample compiles end-to-end from a
one-sentence objective.

**Paper hook.** Scales from small programs to small *systems*; this is
where the quantitative claims live.

### B4 — Retrieval and typed-hole completion for Composer

**Objective.** Cap Composer prompt size regardless of helper count.

**Deliverables.**

- Helper retrieval: given a composition step, retrieve top-K most relevant
  helpers (embedding similarity on `intent` strings + signature-match
  heuristic).
- Typed-hole Composer: for large main bodies, emit a skeleton with typed
  holes (`(?? : Num)`) and fill each hole in a separate LLM call. Each
  hole-fill is tested in isolation using the existing probe mechanism.
- Tests: synthetic Composer input with 30+ helpers; assert prompt size
  stays bounded.

**Acceptance.** A 30-helper sample composes successfully; Composer prompt
never exceeds ~8K tokens regardless of helper count.

**Paper hook.** Demonstrates scaling profile; quantitative result
("composition stays reliable up to N helpers").

## Arc C — The repositioning: capabilities and audit

This is the novel contribution. Without Arc C, there is no paper — only an
engineering report.

### C1 — Capability FFI (`extern defun`)

**Objective.** Expose host C# libraries to the DSL as typed capabilities
that the LLM can compose but cannot invent.

**Deliverables.**

- New syntactic form:
  `(extern defun name ((a : T) (b : U)) : V @capability "http.fetch")`,
  declared in stdlib modules or trusted user capability manifests.
- `Agentic.Core/Capabilities/CapabilityRegistry.cs` — registry of declared
  capabilities; implementations live in `Stdlib/` or user-supplied adapters.
- Verifier and Transpiler route calls to externs through the registry;
  unregistered names error at verify time.
- Rewrite existing stdlib modules (`HttpModule`, `FileModule`, etc.) as
  capability declarations with backing adapters, instead of hard-coded
  verifier intrinsics.
- Sample: `WeatherFetcher.ag` uses `http.fetch`; the generated binary
  embeds the capability manifest.

**Acceptance.** The LLM cannot call a host function that is not a declared
capability. Attempting to declare a capability in user code fails — only
stdlib and trusted manifests may declare them.

**Paper hook.** The LLM's contribution is confined to a
capability-composition problem; ecosystem access is mediated and declared.

### C2 — Default-deny permission gate

**Objective.** Every capability is permission-gated; default is deny;
permissions are granted at compile-time and embedded in the binary.

**Deliverables.**

- Extend `Permissions.cs` into a capability-aware permission model: each
  capability declares a permission class (`http`, `file`, `env`, `process`,
  `time`).
- Verifier rejects capability calls without matching permission before
  transpilation.
- Transpiler emits a startup check in the generated binary that verifies
  runtime permissions match what was granted at compile-time. Binary
  refuses to run if tampered.
- CLI: `--allow-http`, `--allow-file`, `--allow-env`, `--allow-process`,
  `--allow-time`. Default: deny all.
- Tests: using `http.fetch` without `--allow-http` → compile fails.
  Tampered binary → runtime refuses.

**Acceptance.** A compiled binary's capability set is fixed at compile
time and verifiable post-hoc.

**Paper hook.** Permission gating is end-to-end; the binary is
self-describing about what it can do.

### C3 — Mock-driven verification

**Objective.** Tests exercise capability-using code without real I/O, so
verification is deterministic and sandbox-safe.

**Deliverables.**

- Extend `(test …)` to accept a `(mocks …)` subsection:
  ```
  (test fetch-weather
    (mocks (http.fetch "https://api/weather" "{\"temp\": 20}"))
    (assert-eq (fetch-weather) 20))
  ```
- Verifier installs mocks for the duration of the test; real
  implementations are bypassed.
- `--allow-real-io` CLI flag enables real I/O during tests (opt-in).
  Default path is mocks-only.
- Tests: a sample with `http.fetch` and `file.read` that passes
  verification with zero real I/O.

**Acceptance.** CI can compile and verify programs that use capabilities
with no network or filesystem access.

**Paper hook.** Hermetic verification — capability manifest plus mocks
fully specify behavior for audit purposes.

### C4 — Proof-carrying binaries

**Objective.** The compiled binary embeds its own tests, capability
manifest, and contracts, so an auditor can re-verify from the artifact
alone.

**Deliverables.**

- Transpiler emits an embedded resource `manifest.json` containing:
  - Source-level tests as structured data (not raw AST).
  - Capability list with granted permissions.
  - All `(require …)` and `(ensure …)` contracts with source locations.
  - A hash of the generating `.ag` source.
- `agc verify <binary>` subcommand: extracts the manifest, re-runs tests
  against the binary's compiled functions, checks contract invariants.
- Sample: compile `Calculator.ag` → run `agc verify Calculator` → shows
  test-pass report and capability set.

**Acceptance.** `agc verify` on a binary compiled on machine A passes on
machine B with no access to the source.

**Paper hook.** "Proof-carrying" is literal; the artifact is the proof.

## Arc D — Evaluation and paper

### D1 — Benchmark suite designed around the thesis

HumanEval and MBPP measure task completion, not auditability. Build a
suite that measures what the thesis claims.

**Deliverables.**

- `bench/` directory with ~30 problems: natural-language objective +
  acceptance tests. Coverage: pure logic (parsing, transformation),
  capability-using (HTTP, file, time), multi-helper decomposition (5–15
  helpers).
- For each problem, track: pass rate, decomposition depth (helpers,
  modules), attempts/tokens/wall-time to first green, capability set,
  source size.
- Baseline: same problems solved by LLM-writes-Python with pytest as the
  retry oracle. Same metrics.

### D2 — Auditability study

The thesis claims "easier to audit." Test it.

**Deliverables.**

- 10 problems solved by both AgenticCompiler and the Python baseline.
- 3–5 reviewers (colleagues acceptable for a draft). Each reviewer gets
  randomly-assigned programs in both forms. Measure time-to-audit and
  audit-error rate (missed a seeded bug).
- Seed 3 classes of bugs: off-by-one, unchecked input, unexpected I/O. The
  capability manifest should make the third class trivial to catch in
  `.ag` form; Python requires grep-for-imports.

### D3 — Write-up

Structure:

1. Motivation — LLM-authored code is untrusted; existing agent-coding
   projects optimize pass rate, not auditability.
2. Architecture — thesis, capability FFI, hierarchical decomposition,
   proof-carrying binaries.
3. Evaluation — benchmark results, auditability study.
4. Ablations — without reflection loop (B1)? without retrieval (B4)?
   without capability manifest (C1)?
5. Limitations and future work — honest section; see Open questions.

Venue options (decide after results):

- PLDI / OOPSLA (PL community, compiler framing).
- ICSE (SE community, agent framing).
- arXiv + workshop first if results are preliminary.

## Critical path and sequencing

```
A1 (canonical serialization) ─┐
A2 (type plumbing)            ├─► B1 (reflection) ─► B2 (Planner)
A3 (higher-order)             ─┘                            │
                                                            ▼
                                                    B3 (hierarchical)
                                                            │
                                                            ▼
                                                    B4 (retrieval)

C1 (capabilities) ─► C2 (permissions) ─► C3 (mocks) ─► C4 (proof-carrying)
```

Arcs A and C can proceed in parallel. B depends on A. D depends on all.

**Minimum viable paper.** A1–A3 + B1, B2 + C1–C4 + D. B3 and B4 strengthen
the quantitative claim but can defer to a follow-up if time is tight.

**Suggested order of execution (two-track).**

1. Start A1 today — high leverage, 1–2 days.
2. In parallel, spec C1 as a design doc. It touches many files; plan
   before coding.
3. Land A2, A3.
4. Land C1 once A1/A2 are in — records/arrays across boundaries are
   needed for realistic capabilities.
5. B1 before B2 — reflection loop is infrastructure the Planner uses.
6. B2, C2, C3 in parallel once B1 and C1 land.
7. C4 last in Arc C — depends on everything else being stable.
8. B3, B4, D1 in parallel once the core is done.
9. D2, D3 at the end.

## Open questions and risks

**Thesis risk.** "Small DSL is easier to audit than Python" is intuitive
but not proven. If the auditability study returns null, pivot to a weaker
claim ("capability manifests enable static reasoning that Python imports
don't"). Have the backup framing ready.

**Containment risk.** The DSL has no `eval`, reflection, or raw memory —
good. But transpilation to C# could leak host semantics. Audit the
transpiler output for escape hatches before making containment claims.
This is itself paper-worthy if you can prove a property.

**Benchmark risk.** Do not compare against HumanEval — that game is lost
to models that write whole programs in one shot. The benchmark must be
designed for the thesis, not inherited.

**Single-provider coupling.** Currently Gemini-only. Before publication,
factor `IAgentClient` into a provider-agnostic interface with at least
one additional backend (Anthropic or OpenAI). Otherwise the work looks
like "a Gemini feature."

**Scope creep.** Every arc has tempting adjacent work (IDE plugin, LSP,
debugger, REPL). Ruthlessly defer — none of that is on the paper critical
path. Write them down in a "post-paper" section and stop.

## Definitions of "done"

- **Arc A** done when all helpers in all existing samples can be typed
  with non-`double` params and the canonical serializer round-trips every
  `AgType`.
- **Arc B** done when a bare objective produces a multi-module,
  multi-helper program with zero author-declared structure, verified
  against acceptance tests.
- **Arc C** done when `agc verify <binary>` on a compiled artifact
  re-runs tests and prints the capability set, with no access to source.
- **Arc D** done when a draft paper, reproducible benchmark, and
  auditability-study data are all committed to the repo.
