# C5 — Safety policy for `agc-check`

**Status:** frozen (Week 2 of IMPLEMENTATION_PLAN).
**Scope:** the complete set of guarantees the independent checker
`agc-check` delivers on a compiled Agentic program. This is the paper's
threat-model statement — §3.1 of the writeup.

## 1. Subject

The checker operates on a triple

```
    Π = (β, σ, μ)
```

where

- `β` is a self-contained native binary produced by `agc build`,
- `σ` is the `.ag` source embedded in the manifest (exact bytes),
- `μ` is the manifest — a `ProofManifest` record with fields
  `SchemaVersion, SourceHash, BinaryHash, Capabilities, Permissions,
  Tests, Contracts, BuiltAt`.

`agc-check Π` returns `accept` or `reject(r)` for some reason `r`. This
document fixes what `accept` is supposed to mean.

`BinaryHash` is added in **C6**; prior to C6 the binding from `β` to
`μ` is via `SourceHash` alone. Every guarantee below is stated relative
to the post-C6 manifest shape.

## 2. Preconditions on `μ`

Before the three guarantees are evaluated, the checker enforces
well-formedness. `agc-check Π` rejects if any of the following fail:

- **WF1** `μ.SchemaVersion = "1.0"`.
- **WF2** `SHA256(σ) = μ.SourceHash`.
- **WF3** `SHA256(β) = μ.BinaryHash` *(C6)*.
- **WF4** For every `c ∈ μ.Capabilities`, there exists a permission
  entry `p ∈ μ.Permissions` such that `c.Permission = p`.
- **WF5** The source `σ` parses under the grammar of E1.
- **WF6** Every `(test …)` body in `σ` whose name appears in
  `μ.Tests` uses only constructs within the E1 subset
  (`docs/semantics.md` §8.1). Tests that fall outside this subset are
  not required to pass and are not counted in conformance.

If any WF-check fails the verdict is `reject(well-formedness)` and the
three guarantees are not evaluated.

## 3. Guarantees

The three guarantees `agc-check` delivers on `accept`.

### 3.1 Capability soundness (**CS**)

> For every capability call `c` that the binary `β` can make on any
> execution, `c ∈ μ.Capabilities`.

Formalised: let `Syscalls(β)` denote the set of capabilities whose
emit-expressions appear in the disassembly of `β` (extracted by
`CapabilityExtractor` in C7). Then

```
    Syscalls(β)  ⊆  μ.Capabilities
```

**Trust assumption TA-1.** `CapabilityExtractor`'s pattern set is a
sound over-approximation of the set of capabilities the binary can
actually invoke. Documented in `docs/tcb.md` (C9).

### 3.2 Test conformance (**TC**)

> For every `t ∈ μ.Tests`, running `t.SourceSnippet` in the reference
> operational semantics of E1, against the mock frame declared in `t`
> and the declared capabilities `μ.Capabilities`, reduces to a state
> whose test log entry for `t` is `pass`.

Formalised: let `⟶*_E1` denote the transitive closure of the reduction
relation defined in `docs/semantics.md` §4. Let `σ₀(t)` be the initial
store built from `μ.Capabilities` (as `σ.φ`) and the mock bindings
parsed out of `t.SourceSnippet`. Then

```
    ∀ t ∈ μ.Tests.
        (t.SourceSnippet, σ₀(t))  ⟶*_E1  (0, σ')
        ∧  (t.Name, pass) ∈ σ'.τ
```

The checker's `ReferenceInterpreter.cs` is a faithful, byte-literal
implementation of E1 §4.1 – §4.17. Every reduction path carries an
`// E1-rule-N` comment so auditors can map code to semantics.

**Trust assumption TA-2.** The emitted binary `β` implements E1
semantics for the subset covered by `μ.Tests`. We do not re-verify this
at check time — closing the gap is an explicit future-work item (see
`docs/soundness.md` §E3, "emitter-semantics gap").

### 3.3 Contract validity (**CV**)

> For every contract `k ∈ μ.Contracts`, on every execution path the
> reference semantics can reach from any `(test …)` in `μ.Tests`, the
> contract's condition reduces to a truthy value — never to
> `⟦ABORT⟧`.

Formalised: let `Paths(t)` be the set of `⟶*_E1`-reachable configurations
from `(t.SourceSnippet, σ₀(t))`. Then

```
    ∀ k ∈ μ.Contracts. ∀ t ∈ μ.Tests. ∀ π ∈ Paths(t).
        π contains a (require/ensure) redex for k
            ⇒  it reduces to 0  (not to ⟦ABORT⟧)
```

CV reduces to TC: a contract is modeled in C8 as a degenerate test
whose body asserts the condition. If every such test passes under TC,
every reachable contract holds under CV.

## 4. Non-goals

These are **not** guaranteed by `accept`. The checker is silent about
them; users who need them must supply separate evidence.

- **NG-1 Termination.** A test that diverges is stuck and therefore
  fails TC. But the checker makes no claim about the running binary's
  termination in production (non-test paths).
- **NG-2 Functional equivalence outside tests.** `β` may compute any
  function on inputs not exercised by `μ.Tests`. TC covers the
  declared test set, nothing more.
- **NG-3 Memory safety.** Delivered by the emitter target (C# 8 AOT),
  not by the checker.
- **NG-4 Side-channel resistance.** Timing, caching, speculative
  execution, etc., are outside the model.
- **NG-5 Resource bounds.** The checker does not bound memory, CPU,
  or file-descriptor use of `β`.
- **NG-6 Concurrency safety.** The E1 semantics is sequential.
  Programs that introduce parallelism outside the E1 subset are
  `reject(well-formedness)` at WF6.
- **NG-7 Supply-chain integrity.** The checker binds to `β` by
  `BinaryHash`; it does not verify the provenance of the toolchain
  that produced `β`.

## 5. Terminology cleanup

Throughout the codebase and documentation, the three-guarantee bundle
above is referred to as **proof-carrying** (the `P` in "PCC-style"
paper framing). The manifest data structure itself is referred to as
a **capability manifest** — it carries the evidence the checker needs,
but is not itself a proof object. Use:

- "proof-carrying" only where CS + TC + CV are all at stake.
- "capability manifest" when referring to the `ProofManifest` record.
- Avoid "verified binary" — we do not claim deductive verification of
  `β` in its entirety.

## 6. Internal consistency

Each guarantee names exactly one clause of the paper:

| Guarantee | Checker code path (C7)                  | Paper §     |
|-----------|------------------------------------------|-------------|
| CS        | `CapabilityExtractor.Extract` ⊆ `μ`     | §3.2        |
| TC        | `ReferenceInterpreter.Run(t, μ)` = pass | §3.3        |
| CV        | TC applied to each `k ∈ μ.Contracts`    | §3.4        |

Every trust assumption is named (TA-1, TA-2) and listed in
`docs/tcb.md` (C9).

## 7. What `accept` means, in one sentence

> `accept` means: the binary `β` will only use capabilities the
> manifest declares, and every declared test and contract evaluates
> successfully under a reference semantics the auditor can read in
> `docs/semantics.md` — subject to two named trust assumptions.

## 8. References

- `docs/semantics.md` — the E1 reference semantics TC and CV rely on.
- `docs/effects.md` (E2) — will strengthen CS by typing capability
  effects statically; written in Week 3.
- `docs/soundness.md` (E3) — meta-theorems and the emitter-semantics
  gap (TA-2).
- `docs/tcb.md` (C9) — TCB inventory, trust-assumption audit.
- ROADMAP.md Arc C5 — this document's home.
