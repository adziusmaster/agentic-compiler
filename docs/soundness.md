# E3 — Soundness of `agc-check`

**Version.** 1.0 (frozen 2026-04-22, Arc E3).
**Companion docs.** `docs/semantics.md` (E1 — reduction),
`docs/effects.md` (E2 — typing + effects),
`docs/safety-policy.md` (what `agc-check` promises),
`docs/tcb.md` (what is trusted).

This document ties the formal foundations (E1 + E2) to the verdict of
the checker. It states one top-level theorem, decomposes it into three
clauses that mirror the three guarantees of the safety policy (CS, TC,
CV), and sketches the proof of each clause — reducing everything to
three named axioms that are either cited to prior art or marked as
future-mechanization work.

---

## 1 · Setup

### 1.1 Subject

`agc-check` consumes a triple `Π = (β, σ, μ)`:

- `β` — the emitted binary (a self-contained AOT executable).
- `σ` — the Agentic source (an `.ag` file); optional via `--source`.
- `μ` — the capability manifest (sidecar `<β>.manifest.json`).

### 1.2 The emitter semantics gap

The binary `β` does not directly implement E1. It is C# code compiled
to native, produced by `Agentic.Core.Transpiler.Transpile`. We close
this gap by assumption:

> **TA-E (emitter faithfulness).** For every well-typed program `p` in
> the E1 subset (`semantics.md` §2, §8.1), every execution of
> `Transpile(p)` produces the same observable test-log and capability
> invocations as `p` under `⟶*_E1`.

This is **TA-2** in `safety-policy.md` and is out of scope for a paper
pen-and-paper sketch — a mechanized proof would be a separate paper
(E4). In the absence of such a proof, the emitter is audited: every
E1 rule has a corresponding C# emission pattern cross-reviewed when
added.

### 1.3 Other axioms

- **TA-X (extractor soundness).** For every capability `c ∈
  DefaultCapabilities`, the emitter's `CSharpEmitExpr(c)` contains at
  least one substring from `CapabilityExtractor.Patterns[c]`. Stated
  in `safety-policy.md` as TA-1 and in `tcb.md` §2.
- **TA-H (SHA256 CR).** SHA256 is collision-resistant at the input
  sizes in scope (≤ 100 MB binaries, ≤ 1 MB sources, ≤ 64 KB
  manifests). Cited to prior art (FIPS 180-4, academic analysis of
  SHA-2).

---

## 2 · Main theorem

> **Theorem TH-Check (soundness of `agc-check`).** For any triple
> `Π = (β, σ, μ)` such that `Checker.Run(β, σ, safety) = Accept`,
> the three guarantees CS, TC, CV defined in `safety-policy.md` §3
> all hold of `Π` — assuming TA-E, TA-X, TA-H.

The theorem is proved in three parts, one per guarantee. A `strict`
policy acceptance implies `safety` acceptance plus the additional
no-unused-capability condition (`safety-policy.md` §3.1) — the extra
clause is immediate from the checker's code and is not proved
separately.

---

## 3 · Clause 1 — Capability soundness (CS)

> **TH-CS.** If `Accept(Π)` then for every syscall `s` that `β` may
> perform during execution, there exists a capability
> `c ∈ μ.Capabilities` with `s ∈ Impl(c)`.

Where `Impl(c)` is the set of syscalls the C# emission of `c` is
permitted to make (a small, fixed set per capability — e.g.,
`Impl(file.read) = { open(O_RDONLY), read, close }`).

**Proof sketch.**

1. By `Checker.Run` line 59, `Extract(β) ⊆ μ.Capabilities` whenever
   the verdict is `Accept`. (Otherwise `Reject("capability-undeclared")`
   is emitted.)
2. By **TA-X**, the pattern scan inside `Extract` is a sound
   over-approximation: if `β` calls capability `c`, the scan reports
   `c`. Equivalently: `syntactic-caps(β) ⊆ Extract(β)`.
3. By **TA-E**, the capabilities `β` actually invokes at runtime are
   exactly those named by `(extern defun …)` forms in `σ`. Formally:
   `dynamic-caps(β) = static-caps(σ)`.
4. By E2 §4.4 (effect soundness), `static-caps(σ) ⊆ Φ(main)`, the
   inferred effect of the program's main body.
5. By E2 §5 (inference) and the transpiler's cap-collection pass,
   `Φ(main) ⊆ syntactic-caps(β)` — every `{c}` introduced by a
   **T-extern-call** emits the corresponding C# pattern.
6. Composing (2)–(5): `dynamic-caps(β) ⊆ syntactic-caps(β) ⊆
   Extract(β) ⊆ μ.Capabilities`. Every syscall `s` corresponds to some
   `c ∈ dynamic-caps(β) ⊆ μ.Capabilities`, which is what TH-CS
   asserts. ∎

The chain has three links that are axioms (TA-X step 2; TA-E step 3)
and two links that are mechanically checkable (steps 1, 6 — in the
checker's code — and step 5 — in the transpiler). The residual trust
is exactly TA-X + TA-E; no additional axiom is hidden in the proof.

---

## 4 · Clause 2 — Test conformance (TC)

> **TH-TC.** If `Accept(Π)` then for every test
> `t ∈ μ.Tests`, reducing `t`'s body under E1 (with the defs embedded
> in `μ.Defs`) yields a `(name, pass, …)` entry in the test log.

**Proof sketch.**

1. `Checker.Run` invokes `RunTC(μ, σ)`. By its control flow, an
   `Accept` verdict is emitted only when `passed == μ.Tests.Count`,
   which in turn requires every test's log entry to have status `pass`
   (E1 §4.16 `[test-pass]`).
2. The checker's `ReferenceInterpreter` is written to match
   `semantics.md` §4.1–§4.17 literally — every `Eval*` method is
   tagged with `// E1-rule §4.N`. Audit procedure in `tcb.md` §6.5.
3. Reducing `t` under `ReferenceInterpreter` is therefore a faithful
   realization of `⟶*_E1`. A `pass` entry in the log is exactly E1's
   `[test-pass]` rule premise.
4. Thus `Accept(Π) ⇒ t ⟶*_E1 (log := … ⊕ (t, pass, _))` for every
   `t ∈ μ.Tests`. ∎

TH-TC does **not** depend on TA-E — the test is run by the checker's
own interpreter, not by `β`. TH-TC is the one clause of TH-Check that
stands on E1 alone; if the emitter is buggy, TC still holds (the buggy
binary will fail in production but pass the checker, which is exactly
the situation the paper motivates).

*Audit caveat.* Step 2 depends on every rule-tag being correct. The
cross-reference is enforced by convention only — any future rule
renumbering must touch both `semantics.md` and
`ReferenceInterpreter.cs` in a single commit (see memory-note on rule
numbering discipline).

---

## 5 · Clause 3 — Contract validity (CV)

> **TH-CV.** If `Accept(Π)` then for every `(require r)` or
> `(ensure e)` attached to a function `f` in `μ.Contracts`, and for
> every execution trace of `f` reached by any `t ∈ μ.Tests` under E1,
> the contract holds.

**Proof sketch.**

1. By E1 §4.17, `(require r)` aborts reduction with `ContractAbort`
   when `r` reduces to `false`. Symmetrically for `(ensure e)`.
2. In the checker's reference interpreter, `ContractAbort` surfaces as
   a `fail` entry in the test log (`ReferenceInterpreter.RunTest`,
   `catch (ContractAbort)` branch).
3. By TH-TC, every `t ∈ μ.Tests` produces a `pass` entry. Combined
   with (2), no contract aborted during any test's reduction.
4. The checker additionally validates that each contract parses under
   E1 (`Checker.Run` lines 82–90). This rules out structurally
   malformed contracts that could trivially hold by never executing.
5. Thus every `(require r)` / `(ensure e)` in `μ.Contracts` held on
   every execution path reached by `μ.Tests`. ∎

**Coverage caveat.** CV is quantified over *reached* execution paths,
not all paths. A contract on `f` is validated iff some `t ∈ μ.Tests`
exercises `f`. Coverage is an obligation on the *test author* — the
checker cannot synthesize missing tests. This is stated as NG-2 in
`safety-policy.md` and is why D1's benchmark suite weights test
coverage alongside TC pass-rate.

---

## 6 · Composing the three clauses

```
             TA-E         TA-X           TA-H
               │            │              │
               ▼            ▼              ▼
             TH-CS       (implicit in Extract)   (implicit in WF3)
               │
               ├──────┐
               ▼      ▼
             TH-TC  TH-CV
               │      │
               └──┬───┘
                  ▼
               TH-Check
```

- TH-CS depends on TA-E + TA-X.
- TH-TC depends on the reference interpreter's faithfulness to E1
  (audit obligation; no axiom).
- TH-CV depends on TH-TC plus E1's contract rules (§4.17).
- WF1 / WF2 / WF3 (schema / source-hash / binary-hash) are
  closed-form checks that rely on TA-H (SHA256 CR).

The proof has **no hidden axioms**: every link is either cited (TA-H),
audited (TA-E, TA-X, and the rule-tag correspondence), or
syntactically checkable in the TCB (every `Reject` path in `Checker.Run`).

---

## 7 · What the paper claims

- **Claimed by Section 5 (pen-and-paper).** TH-CS, TH-TC, TH-CV,
  TH-Check — as sketched here.
- **Assumed (axioms).** TA-E, TA-X, TA-H.
- **Not claimed.** Termination (NG-1), memory safety beyond what the
  C# runtime provides (NG-3), side-channel freedom (NG-4), concurrency
  (NG-6). See `safety-policy.md` §4.
- **Future work (E4, post-paper).** Mechanize TH-TC and TH-CV in a
  proof assistant (Rocq or Lean); mechanize TA-E by expressing the
  transpiler as a verified function; extend E2 with HOF (E2.1),
  modules (E2.2), and refinement effects (E2.3).

---

## 8 · Review checklist

- [x] The three axioms TA-E / TA-X / TA-H are named, and only these
      three are relied on.
- [x] Every step of every proof sketch cites either an E1 rule, an E2
      rule, a line number in `Checker.Run`, or an axiom.
- [x] TH-TC does not depend on TA-E (so a buggy emitter cannot cause
      the checker to accept a failing test).
- [x] The coverage caveat on TH-CV is stated explicitly.
- [x] Cross-references to `safety-policy.md` (non-goals, CS/TC/CV
      definitions), `semantics.md` (rule numbers), `effects.md`
      (typing + effect soundness), and `tcb.md` (audit procedure).

## 9 · References

- `docs/semantics.md` — E1 reduction relation (§4) and meta-property
  statements (§7).
- `docs/effects.md` — E2 typing + effect soundness (§3, §4.4).
- `docs/safety-policy.md` — policy guarantees CS/TC/CV and trust
  assumptions TA-1/TA-2 (= TA-X/TA-E).
- `docs/tcb.md` — audit procedure for every axiom.
- FIPS 180-4. *Secure Hash Standard*. NIST, 2015. — for TA-H.
- Gifford, Lucassen. *Integrating functional and imperative
  programming*. LFP '86. — effect systems prior art.
- Nielson, Nielson, Hankin. *Principles of Program Analysis*, Ch. 5.
