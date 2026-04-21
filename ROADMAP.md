# AgenticCompiler — Foundation-to-Paper Roadmap

This document is the implementation plan that takes AgenticCompiler from its
current proof-of-concept state to a system with a defensible paper-worthy
thesis. It supersedes the in-flight `Stage 1 / 2 / 3` plan from
`~/.claude/plans/awesome-glad-you-like-glittery-oasis.md` where they conflict.

## Thesis

The claim worth defending:

> AgenticCompiler is the first AOT-compiled language purpose-built for LLM
> authorship. Its S-expression surface is small enough for an agent to
> generate correctly, its test-gated hermetic compiler refuses to emit a
> binary that fails a declared test, and every I/O action is a declared,
> permission-gated capability.
>
> The compiled binary carries a manifest of its source, tests, capabilities,
> and contracts, and is accepted by an **independent minimal-TCB checker
> (`agc-check`)** against a formal safety policy. If the checker accepts, the
> binary is guaranteed — against a reference operational semantics — to:
> (a) invoke only declared capabilities,
> (b) pass its embedded tests in the reference semantics, and
> (c) honor its `require` / `ensure` contracts on every reachable path.

This is **verification-condition proof-carrying code** (Tier B PCC), not a
self-attested manifest: the checker is a separate binary with ~1 k LOC and no
dependency on the compiler, so trust collapses onto the checker and the
formal semantics, not onto the generator.

The project occupies a niche no other language targets directly: existing
AI-oriented languages (Mojo, Bend, Codon) optimize for performance or
parallelism, not agent-safety; existing agent-coding tools (Cursor, Devin,
Aider) are tools over general-purpose languages, not languages. Success
criterion: a human auditor can certify a 200-line `.ag` program faster than
the equivalent Python *and* can run `agc-check` to mechanically confirm that
the certification still holds on any binary claiming to be built from that
source.

### Candidate paper framings

- "AgenticCompiler: A Proof-Carrying Native Language for LLM-Authored Code"
- "Capability-Gated LLM Synthesis with an Independent Verifier"
- "Verification-Condition PCC for Hermetically-Verified Agent Programs"

Pick a framing once the evaluation (Arc D) results are in; do not pre-commit.

## Structure

Five arcs. A–C + E are the technical contribution; D is the paper.
A, C, and E are on the critical path. B strengthens the quantitative claims.
D is mandatory.

- **Arc A** — Decomposition unblockers (type plumbing, capability breadth).
- **Arc B** — Hierarchical decomposition + reflection loop.
- **Arc C** — Capability FFI, permission gate, proof-carrying artifacts, and
  the **independent checker `agc-check`** that makes PCC real.
- **Arc E** — Formal foundations: operational semantics, type-and-effect
  system, soundness sketch. Makes Arc C's guarantees mean something.
- **Arc D** — Benchmark, auditability study, paper.

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

### A4 — Capability registry breadth

**Objective.** The registry covers enough real-world I/O surface to make the
evaluation credible. Current registry (http.fetch, time.now_unix) is a demo.

**Deliverables.**

- Extend `DefaultCapabilities.BuildTrusted()` with:
  - `file.read` (permission: `file`) — reads UTF-8 text; mock-safe adapter.
  - `file.write` (permission: `file`) — atomic write; mock-safe adapter.
  - `env.get` (permission: `env`) — reads environment variable.
  - `db.query` (permission: `db`) — SQLite query; mock returns rows.
  - `process.spawn` (permission: `process`) — runs subprocess, returns
    stdout + exit code; mock returns pre-canned output.
- Each capability: registered with param/return `AgType`s, emit-expression
  for the transpiler, permission string, and a mock-safe adapter.
- Samples: one `.ag` file per capability that exercises the capability
  under a `(mocks …)` clause and through `--allow-*` for real I/O.
- Tests: `CapabilityRegistryBreadthTests` — each capability parses,
  permission-gates, and runs against mocks.

**Acceptance.** The benchmark suite (D1) can cover capability-using problems
beyond HTTP without stubbing the registry per-problem.

**Paper hook.** Capability coverage is breadth-of-ecosystem, not a demo.

### A5 — Stdlib/test consistency audit

**Objective.** Close remaining test/impl mismatches so the test suite is a
trustworthy regression signal for Arc D.

**Deliverables.**

- Fix `JsonGet_MissingKey_ShouldFail` — decide: either `json.get` throws on
  missing key (align implementation), or the test is wrong (align test).
  Document the chosen semantics in `LanguageSpec.cs`.
- Sweep remaining stdlib modules for similar mismatches; no red tests.
- Update CLAUDE guidance in `ROADMAP.md` "Open questions" if a decision was
  load-bearing.

**Acceptance.** `dotnet test` passes 100 %; no skipped tests.

**Paper hook.** Hygiene — reviewers will grep `dotnet test` output.

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

### C5 — Formal safety policy

**Objective.** Write down — precisely — what `agc-check` guarantees. Without
this, Arc C is a collection of engineering conveniences, not a theorem.

**Status (2026-04-21):** frozen. Live at
[`docs/safety-policy.md`](docs/safety-policy.md). Defines the checker's
subject `Π = (β, σ, μ)`, six well-formedness preconditions WF1–WF6, the
three guarantees CS (capability soundness) / TC (test conformance) /
CV (contract validity) as predicates on `Π`, seven enumerated non-goals
NG-1–NG-7, and two named trust assumptions TA-1 (capability-extractor
soundness) / TA-2 (emitter implements E1).

**Deliverables.**

- `docs/safety-policy.md` — a specification of the three guarantees the
  checker delivers:
  1. **Capability soundness.** Every syscall the program makes corresponds
     to a capability declared in the manifest.
  2. **Test conformance.** Every `(test …)` in the manifest, re-run in the
     reference semantics against the manifest-embedded source, passes.
  3. **Contract validity.** Every `require` holds at every call site and
     every `ensure` holds at every return site, in the reference
     semantics.
- Explicit non-goals: termination, functional equivalence between `.ag` and
  emitted binary outside of declared tests, memory safety (C# AOT gives
  us that free), side-channel resistance.
- Rename cleanup: in README and manifest, use "proof-carrying" only where
  the above guarantees apply; use "capability manifest" elsewhere.

**Acceptance.** The policy is short (one page), precise (each property is
stated as a condition on a tuple `(binary, source, manifest)`), and
internally consistent (no property depends on a guarantee the checker
can't provide).

**Paper hook.** Section 3.1 of the paper is this document.

### C6 — Binary-hash binding in manifest

**Objective.** Detect post-emission tampering: if anyone edits the binary
after compilation, `agc verify` / `agc-check` must reject it.

**Status (2026-04-21):** shipped. `ProofManifest` gained a `BinaryHash`
field; `Compiler.Compile` writes a sidecar `<binaryPath>.manifest.json`
holding the full manifest including `BinaryHash = SHA256(β)` (the
embedded copy inside the binary omits this field due to the chicken-
and-egg: a binary cannot contain its own hash in plaintext). `agc verify`
prefers the sidecar, recomputes SHA256 on disk, exits 1 with
`binary-tampered` if mismatched. End-to-end smoke test on
`samples/Calculator.ag`: untampered → exit 0; flipping one byte in the
emitted binary → exit 1 with both declared and actual hashes printed.
9 unit tests in `BinaryHashTests`. 343/343 suite green.

**Deliverables.**

- `NativeEmitter.Emit` computes SHA256 of the emitted binary file after the
  final link step and stores it in the manifest as `BinaryHash`.
- Two-hash binding:
  - `SourceHash` — SHA256 of `.ag` source (already present).
  - `BinaryHash` — SHA256 of the binary file at emission time.
- `agc verify <bin>` recomputes SHA256 of the binary on disk, compares to
  `BinaryHash` in the manifest, fails if mismatch.
- Tests: tamper with a bit in the emitted binary → `agc verify` exits
  non-zero with `binary-tampered` diagnostic.

**Acceptance.** Round-trip: compile → hash → ship → re-verify on a
different machine → pass. Tamper any byte → fail.

**Paper hook.** Closes the "manifest is self-attested" critique: the
manifest binds to a specific byte-exact binary.

### C7 — Independent checker `agc-check`

**Objective.** Build the checker. It must be a separate binary, have no
dependency on `Agentic.Core`'s transpiler, and be small enough to audit.

**Deliverables.**

- New project `Agentic.Check/` — console app. Dependencies: only
  `System.Text.Json` and `System.Security.Cryptography` from BCL. No
  reference to `Agentic.Core`, `Agentic.Cli`, or any stdlib module that
  performs I/O beyond file-read.
- `Agentic.Check/Parser.cs` — mini S-expr parser, ~200 LOC. Reads the
  Agentic subset needed to re-run tests; rejects anything it does not
  recognize.
- `Agentic.Check/ReferenceInterpreter.cs` — evaluator implementing the
  operational semantics from E1. Covers the subset used by `(test …)`
  bodies: numbers, strings, bools, arithmetic, comparisons, arrays, maps,
  defun, defstruct, `assert-eq` / `assert-true` / `assert-near`, `require`,
  `ensure`, and capability calls resolved through **mocks declared in the
  manifest** (never real I/O).
- `Agentic.Check/CapabilityExtractor.cs` — extracts capability call sites
  from the emitted binary by scanning for the transpiler's
  `CSharpEmitExpr` signatures (documented per-capability). Output: a set
  of capability names actually invoked by the binary.
- Checker CLI:
  `agc-check <binary> [--source <file.ag>] [--policy safety|strict]`
  - Reads manifest from binary (`<binary> --verify`).
  - If `--source` supplied: recomputes SHA256 of source, compares to
    `SourceHash` in manifest, fails on mismatch.
  - Recomputes SHA256 of binary, compares to `BinaryHash`, fails on
    mismatch.
  - Runs `CapabilityExtractor` on the binary, compares to manifest
    `Capabilities` — fails if binary invokes anything not declared.
  - For each manifest `Tests[i]`, runs the reference interpreter against
    the source subset and confirms the test passes.
  - For each manifest `Contracts[i]`, re-runs the contract check against
    the reference semantics.
  - Exits 0 with a structured report on full pass; non-zero on any fail.
- Tests (in `Agentic.Check.Tests/`):
  - Happy path: sample compiles, checker accepts.
  - Capability tamper: edit manifest to drop a declared capability while
    the binary still calls it → checker rejects.
  - Test tamper: edit manifest to claim more tests passed than exist →
    checker rejects.
  - Source mismatch: supply wrong `.ag` file → checker rejects.
  - Binary mismatch: flip a bit → checker rejects.

**Acceptance.** Checker LOC ≤ 1500, runtime dependencies ≤ BCL only,
every negative test above rejects with a distinct diagnostic code.

**Status (2026-04-21): shipped.** `Agentic.Check/` delivered as a
standalone BCL-only console app with `AssemblyName=agc-check`:
`Parser.cs` (146 LOC), `Manifest.cs` (63 LOC), `ReferenceInterpreter.cs`
(579 LOC — every reduction path tagged `// E1-rule §4.N`),
`CapabilityExtractor.cs` (63 LOC), `Checker.cs` (182 LOC, testable
entry-point), `Program.cs` (72 LOC, argv + exit-code translation only).
Total **1105 LOC**, well under the 1500 budget. No ProjectReference to
`Agentic.Core`. Tests in `Agentic.Check.Tests/` cover the five required
rejection codes (`binary-tampered`, `source-mismatch`,
`capability-undeclared`, `test-count-mismatch`, plus `io-error` /
`no-manifest`) and happy-path accept — 8 tests, all green alongside the
existing 343 Core tests. End-to-end smoke against `samples/Calculator`
with `--source`: `accept`, 2/2 tests pass.

**Paper hook.** This *is* the independent-checker claim. Section 4 of the
paper.

### C8 — VC emission for contracts and tests

**Objective.** Tests and contracts in the manifest are self-contained
verification conditions — the checker can evaluate them without access to
the source beyond what the manifest embeds.

**Deliverables.**

- Extend `ProofManifest` with:
  - Per test: the **inline source snippet** (already present) plus a
    **capability-mock map** as structured data, not embedded inside a
    `(mocks …)` S-expr string.
  - Per contract: the enclosing function's full AST, plus the contract
    expression, both as structured JSON (the checker parses this, not
    arbitrary `.ag`).
- Transpiler: on emit, walks the AST and writes the above to the manifest.
- Checker: validates each VC by feeding the embedded AST to the reference
  interpreter.
- Tests: a `.ag` file with 3 contracts and 2 tests → manifest contains
  exactly those 5 VCs → checker accepts.

**Acceptance.** The checker needs only `(binary, manifest)` — optional
`--source` is a second line of defense, not a prerequisite. Running
`agc-check <bin>` (no `--source`) still validates tests and contracts.

**Status (2026-04-21): shipped.** `ProofManifest` gained an `EmbeddedDef`
list (`Kind`, `Name`, full untruncated `SourceSnippet`), populated by
`ProofManifestBuilder.Build` from the top-level `defun` / `defstruct` /
`extern` / `def` forms under `(module …)`. Snippet truncation removed —
JSON round-trip now works. Checker reads `Defs` from sidecar and, when
no `--source` is given, pre-evaluates them into a shared reference
interpreter, then runs each test snippet against that interpreter.
`EvalDefun` fixed to synthesise a `(do …)` wrapper when the body has
multiple forms (previously only the last form was evaluated, silently
skipping `require` / `ensure`). Three new Check tests:
`Run_NoSource_WithEmbeddedDefs_AcceptsTestsThatCallUserFunctions`,
`Run_NoSource_WithContractsInEmbeddedDefs_EnforcesRequireAtCallSite`,
`Run_NoSource_ContractViolation_RejectsAsTestFail`. End-to-end smoke:
compile `samples/Calculator.ag` → sidecar contains both `Defs` entries
→ `agc-check <bin>` (no `--source`) → `accept`, 2/2 tests pass. Suite
**354/354** (343 Core + 11 Check). Checker TCB 1153 LOC (still under
1500 budget). Follow-ups deferred: structured capability-mock map
(currently mocks stay as S-expr inside test body); JSON-native AST
per-VC (text snippets through the same parser already satisfy the
self-contained-VC acceptance).

**Paper hook.** This is where "verification-condition" earns the name.

### C9 — TCB audit document

**Objective.** Reviewers will ask: "what do I have to trust?" Answer it in
writing.

**Deliverables.**

- `docs/tcb.md` — enumerates:
  - Lines of code in `Agentic.Check/` (target: ≤ 1500).
  - Every external dependency (should be only `System.Text.Json`,
    `System.Security.Cryptography`).
  - Every I/O capability the checker itself uses (only file-read for
    binary + source + manifest).
  - Every axiom the soundness argument depends on (e.g., "the emitted C#
    faithfully implements the reference semantics for the subset declared
    in the manifest" — see Arc E for how far this can be tightened).
  - Attack surface: what a malicious compiler / binary / manifest can and
    cannot achieve against the checker.
- CI check: fail the build if `Agentic.Check/` LOC exceeds the budget.

**Acceptance.** A reviewer can, in 30 minutes, read the TCB doc and
understand exactly what to trust. The document is one file, not a tour
of the codebase.

**Paper hook.** Section 4.3 — "What the checker trusts." A reviewer-facing
answer to every soundness question in advance.

## Arc E — Formal foundations

Arc C delivers an independent checker. Arc E makes its verdict *mean*
something by grounding it in a formal semantics against which "test passes"
and "contract holds" are defined. Paper-level — no mechanization required
for the submission draft; Coq / Lean is a follow-up.

### E1 — Small-step operational semantics for core Agentic

**Objective.** A reference semantics for the subset used by `agc-check`.
Clear, inspectable, one document.

**Status (2026-04-20):** drafted and frozen pending `ReferenceInterpreter.cs`
stubs. Live at [`docs/semantics.md`](docs/semantics.md) — see §4 for the
reduction relation, §5.1 for `str.*` / `math.*` metafunction tables, §8.1
for construct coverage by sample, and Appendix A for a worked test trace.

**Deliverables.**

- `docs/semantics.md` — small-step SOS rules for the core subset:
  - Values: `Num`, `Str`, `Bool`, `Array`, `Map`, `Record`, closure.
  - Expressions: arithmetic, comparisons, `if`, `while`, `def`, `set`,
    `defun` (first-order only for E1), `defstruct`, `return`, function
    call, capability call, `assert-eq` / `assert-true` / `assert-near`,
    `require`, `ensure`, `(mocks …)`.
  - Explicit out-of-scope for E1: higher-order functions, modules,
    imports. These return in E2 as extensions with their own sections.
- Each rule stated as `E, σ → E', σ'` with the store `σ` capturing the
  environment + mock frame.
- Capability-call rule: `(f args) → v` iff `(f, firstArg) ∈ σ.mocks` and
  `σ.mocks[(f, firstArg)] = v`. Unmocked capability calls are **stuck**
  (not undefined; this is deliberate — mocks are the semantics for
  testing, real I/O has a separate rule guarded by `AllowRealIo`).

**Acceptance.** Every construct the checker's reference interpreter uses
has a corresponding rule. The interpreter is a faithful implementation of
the document.

**Paper hook.** Section 5 (Formalism).

### E2 — Type-and-capability-effect system

**Objective.** A typing judgment `Γ ⊢ e : τ ! Φ` where `Φ` is the set of
capabilities the expression may invoke. Arc C's checker uses this to
reject any binary whose `Φ` exceeds the manifest-declared capability set.

**Deliverables.**

- Typing rules for the core subset.
- Effect-extension rule: every call to `(extern defun f …)` with
  `@capability c` adds `c` to `Φ`.
- Soundness statement: if `Γ ⊢ e : τ ! Φ` and `e → e'`, then
  `Γ ⊢ e' : τ ! Φ'` with `Φ' ⊆ Φ` (progress + preservation +
  effect-monotonicity).
- On-paper proof sketch for the core subset (no HOF, no modules).
- Extension stubs for HOF (E2.1) and modules (E2.2) — out of scope for
  the first paper, but state the extension clearly so the reviewer sees
  the roadmap.

**Acceptance.** Proof sketch is tight enough that a reviewer believes it.
Not yet mechanized — that is E4 (post-paper).

**Paper hook.** Section 5.2. The effect system is the formal counterpart
of the capability manifest.

### E3 — Soundness sketch tying E2 to `agc-check`

**Objective.** State and argue the top-level theorem: if `agc-check`
accepts, the three policy guarantees from C5 hold.

**Deliverables.**

- `docs/soundness.md` — theorem statement:
  > **Theorem (agc-check soundness).** For any tuple `(binary, source,
  > manifest)`, if `agc-check` accepts then:
  > 1. (Capability soundness) every syscall the binary makes corresponds
  >    to a capability `c` with `c ∈ manifest.Capabilities`.
  > 2. (Test conformance) for every `t ∈ manifest.Tests`, `t` passes in
  >    the reference semantics `→*` defined in E1.
  > 3. (Contract validity) for every `(require r)` / `(ensure e)` in
  >    `manifest.Contracts`, the condition holds on every execution path
  >    in the reference semantics.
- Proof sketch per clause:
  - (1) reduces to `CapabilityExtractor` correctness + E2 effect
    soundness.
  - (2) reduces to reference-interpreter faithfulness to E1.
  - (3) reduces to (2) applied to each contract as a test.
- Explicit assumptions, each flagged as **trust-assumption**:
  - The emitted C# implements E1 semantics for the manifest subset.
  - `CapabilityExtractor`'s pattern set covers every capability emit
    expression registered in `DefaultCapabilities`.
  - SHA256 is collision-resistant for the input sizes we care about.

**Acceptance.** The soundness document is reviewable in 1 hour. Every
trust-assumption is either (a) cited to prior art (e.g., SHA256), or
(b) listed as future work to close (e.g., mechanized proof of emitter
faithfulness).

**Paper hook.** Section 5.3 — the theorem that the paper's title promises.

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

**Status (2026-04-20):** A1–A3, B1, B3, B4 (exists as PipelineOrchestrator),
C1–C4, multi-provider client — **done**. The remaining critical path is:

```
A5 (test hygiene) ──┐
A4 (cap breadth) ──┤
                   ▼
                  C5 (safety policy) ──► C6 (binary hash) ──┐
                                                            │
                                                            ▼
E1 (semantics) ──► E2 (effects) ────► C7 (agc-check) ──► C8 (VC emission) ──► C9 (TCB audit)
                                                            │
                                                            ▼
                                                    E3 (soundness sketch)
                                                            │
                                                            ▼
                                                    B2 (autonomous planner)
                                                            │
                                                            ▼
                                                    D1 (benchmarks, 30 problems)
                                                            │
                                                            ▼
                                                    D2 (auditability study)
                                                            │
                                                            ▼
                                                        D3 (paper)
```

Arcs A-hygiene and E1 can run in parallel at the start. C7 is the single
longest-pole engineering task after the prereqs; C8 follows directly. E3
can be drafted while C7/C8 are in flight and finalized once the checker
exists.

**Minimum viable paper.** A4 + A5 + C5–C9 + E1–E3 + D1 + D2 + D3. B2
strengthens the decomposition claim but can defer to a follow-up venue if
the bench timeline binds first.

**Suggested order of execution (two-track).**

1. Land A5 in a day — hygiene, unblocks CI signal.
2. Start A4 and E1 in parallel — A4 is engineering (2 days), E1 is writing
   (1 week). Different contexts, no conflict.
3. Land C5 (formal policy) — 3 days. E1 informs the test-conformance
   clause; do C5 after E1 has a first draft.
4. Land C6 (binary hash) — 1 day. Gatekeeper for the checker.
5. Draft E2 (effect system) while beginning C7. Both need ~1 week; they
   inform each other.
6. Land C7 (the checker) — the biggest task, ~2 weeks. Plan the project
   layout and LOC budget on day 1 (see IMPLEMENTATION_PLAN.md).
7. Land C8 (VC emission) directly after C7 — same context, ~1 week.
8. Finalize E3 soundness doc — ties E2 + C7 + C8 together.
9. C9 (TCB audit) — 2 days. Reviewer-facing, depends on C7 being frozen.
10. B2 (autonomous planner) — optional for the first paper; schedule
    based on remaining time before D1 kickoff.
11. D1 → D2 → D3 per the original plan.

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

**Single-provider coupling.** Resolved — `AgentClient` / `AnthropicClient` /
`OpenAiClient` all implement `IAgentClient`, and the CLI auto-selects based
on the first API key present (priority Anthropic → OpenAI → Gemini),
overridable with `AGENTIC_PROVIDER`.

**TCB blowup.** The checker must stay small (target ≤ 1500 LOC, BCL-only
dependencies). Every feature pushed into `agc-check` costs us credibility.
If a feature is tempting but grows the checker — push it into the compiler
and out of the TCB instead, and add a trust-assumption to `docs/tcb.md`
rather than extending the checker.

**Emitter-semantics gap.** E2 gives us an effect system for `.ag`; the
checker reads capabilities out of the emitted binary. The bridge —
"the emitted C# implements E1 semantics for the manifest subset" — is an
axiom in the first paper, not a theorem. State it clearly as future work.
Closing it (via a Coq-mechanized emitter or an equivalence-checking tool)
is a follow-up paper on its own.

**Non-determinism in emission.** If `dotnet publish` produces
bit-different binaries across machines for the same source,
`BinaryHash` binding is useless for cross-machine verification. Measure
this early in C6 and document. Fallback: bind the hash per-emission, not
per-source, and rely on `SourceHash` for cross-machine claims.

**Scope creep.** Every arc has tempting adjacent work (IDE plugin, LSP,
debugger, REPL). Ruthlessly defer — none of that is on the paper critical
path. Write them down in a "post-paper" section and stop.

## Definitions of "done"

- **Arc A** done when all helpers in all existing samples can be typed
  with non-`double` params, the canonical serializer round-trips every
  `AgType`, the capability registry covers file / env / db / process /
  time / http, and the test suite is green.
- **Arc B** done when a bare objective produces a multi-module,
  multi-helper program with zero author-declared structure, verified
  against acceptance tests. (Today: reflection + pipeline exist; B2
  autonomous planner is the remaining bar.)
- **Arc C** done when `agc-check <binary>` — a separate binary with
  ≤ 1500 LOC and BCL-only dependencies — accepts a compiled artifact
  and, on tampering the binary or manifest, rejects with a distinct
  diagnostic code for each of: source-hash mismatch, binary-hash
  mismatch, undeclared capability, failing embedded test, violated
  contract. The `docs/tcb.md` and `docs/safety-policy.md` are both
  committed and internally consistent.
- **Arc E** done when `docs/semantics.md`, `docs/effects.md`, and
  `docs/soundness.md` are committed, cross-referenced, and match the
  implementation of `agc-check`'s reference interpreter. Every
  trust-assumption is listed in `docs/tcb.md`.
- **Arc D** done when a draft paper, reproducible benchmark, and
  auditability-study data are all committed to the repo.
