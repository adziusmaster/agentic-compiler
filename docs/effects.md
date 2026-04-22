# E2 — Type-and-capability-effect system

**Version.** 1.0 (frozen 2026-04-22, Arc E2).
**Companion docs.** `docs/semantics.md` (E1 — reduction relation),
`docs/safety-policy.md` (what `agc-check` promises),
`docs/soundness.md` (E3 — theorem tying E2 to the checker, pending).

The effect system gives a syntactic account of *which capabilities an
expression may invoke*. It is the formal counterpart of the capability
manifest: if `Γ ⊢ e : τ ! Φ`, then reducing `e` cannot cause any syscall
outside `Φ`.  C5/C7 use this property to reject a binary whose observed
capabilities exceed `μ.Capabilities`.

---

## 1 · Judgment

```
    Γ ⊢ e : τ ! Φ
```

reads *"in context `Γ`, expression `e` has type `τ` and effect at most
`Φ`."* All three pieces are inferred together; the system has no
stand-alone type judgment.

- `Γ`  — a finite map from variable names to type schemes.
- `τ`  — a type drawn from §2.1.
- `Φ`  — a finite set of capability names, drawn from the lattice
        `(𝒫(Cap), ⊆)`. Bottom is `∅`; join is `∪`.

Convention: lowercase `φ` is a specific capability; uppercase `Φ, Ψ`
are effect sets.

## 2 · Types and effects

### 2.1 Type grammar

```
τ ::= Num | Str | Bool | Unit
    | Array τ
    | Map Str τ
    | Record ρ                           (nominal; ρ indexes §3.2 of E1)
    | (τ̄) → τ ! Φ                        (function arrow, with latent effect)
```

The arrow type carries a *latent effect* `Φ` — the capabilities that
will be invoked when the function is called, not when it is merely
mentioned. This is standard Gifford–Lucassen style.

### 2.2 Effect lattice

```
    Φ, Ψ ⊆ Cap               Cap = finite set of registered cap names
    Φ ⊑ Ψ   ≡   Φ ⊆ Ψ        subsumption order
    Φ ⊔ Ψ   ≡   Φ ∪ Ψ        join
    ⊥       ≡   ∅             bottom
```

The lattice is finite (≤ 2^|Cap|), so inference terminates by
monotone convergence.

### 2.3 Subsumption

Every typing rule that produces an effect `Φ` admits

```
    Γ ⊢ e : τ ! Φ      Φ ⊆ Ψ
    ─────────────────────────  [T-sub]
           Γ ⊢ e : τ ! Ψ
```

Subsumption is the only non-syntax-directed rule; type-checking is
bidirectional elsewhere.

---

## 3 · Typing rules (core subset)

Rules mirror the small-step relation in `semantics.md` §4. Each name
carries the matching `§4.N` tag.

### 3.1 Literals and variables (§4.2)

```
    ─────────────────────────  [T-num]       ─────────────────────────  [T-str]
    Γ ⊢ n : Num ! ∅                         Γ ⊢ s : Str ! ∅

    ─────────────────────────  [T-bool]      ─────────────────────────  [T-unit]
    Γ ⊢ b : Bool ! ∅                        Γ ⊢ () : Unit ! ∅

    Γ(x) = τ
    ─────────────────────  [T-var]
    Γ ⊢ x : τ ! ∅
```

Literals and variables are pure: no effect.

### 3.2 Arithmetic and comparison (§4.3)

For each built-in operator `op` with signature `τ₁ × τ₂ → τ`:

```
    Γ ⊢ e₁ : τ₁ ! Φ₁      Γ ⊢ e₂ : τ₂ ! Φ₂
    ─────────────────────────────────────────  [T-op]
         Γ ⊢ (op e₁ e₂) : τ ! Φ₁ ∪ Φ₂
```

Effects compose by union. Order of evaluation does not affect the
typing (but it does affect reduction — see E1 §4.1).

### 3.3 Control flow (§4.4 – §4.5)

```
    Γ ⊢ e₁ : Bool ! Φ₁    Γ ⊢ e₂ : τ ! Φ₂    Γ ⊢ e₃ : τ ! Φ₃
    ──────────────────────────────────────────────────────────  [T-if]
              Γ ⊢ (if e₁ e₂ e₃) : τ ! Φ₁ ∪ Φ₂ ∪ Φ₃

    Γ ⊢ eᵢ : τᵢ ! Φᵢ    (for each i)
    ─────────────────────────────────────────  [T-do]
         Γ ⊢ (do e₁ … eₙ) : τₙ ! ⋃ᵢ Φᵢ

    Γ ⊢ e₁ : Bool ! Φ₁    Γ ⊢ e₂ : Unit ! Φ₂
    ───────────────────────────────────────────  [T-while]
       Γ ⊢ (while e₁ (do e₂)) : Unit ! Φ₁ ∪ Φ₂
```

Note `T-if` takes the union of both branches: the effect is *may*, not
*must*.

### 3.4 Binding and mutation (§4.6)

```
    Γ ⊢ e : τ ! Φ
    ──────────────────────────────────  [T-def]
    Γ ⊢ (def x : τ e) : Unit ! Φ,
    Γ' = Γ, x : τ   (in continuation)

    Γ(x) = τ      Γ ⊢ e : τ ! Φ
    ─────────────────────────────  [T-set]
    Γ ⊢ (set x e) : Unit ! Φ
```

`(set x e)` requires `x` to already be in scope with a matching type,
matching `⟶[set]` in E1.

### 3.5 Functions and calls (§4.7 – §4.8)

```
    Γ, x̄ : τ̄ ⊢ e : τ ! Φ
    ────────────────────────────────────────────  [T-defun]
    Γ ⊢ (defun f (x̄ : τ̄) : τ e) : Unit ! ∅
    Γ' = Γ, f : (τ̄) → τ ! Φ   (in continuation)

    Γ(f) = (τ̄) → τ ! Φf      Γ ⊢ eᵢ : τᵢ ! Φᵢ
    ───────────────────────────────────────────────  [T-call]
         Γ ⊢ (f ē) : τ ! Φf ∪ ⋃ᵢ Φᵢ

    Γ ⊢ e : τ ! Φ
    ──────────────────────────── [T-return]
    Γ ⊢ (return e) : τ ! Φ
```

`T-defun` introduces no effect at the declaration site — defining a
function is pure. The latent effect `Φf` is discharged at each call.

### 3.6 Records and arrays (§4.9 – §4.10)

For brevity: `rec-new`, `rec-get`, `rec-set`, `arr-new`, `arr-get`,
`arr-set`, `arr-length` all follow the pattern of `T-op` — effects
accumulate by union, the type is determined by the record/array
signature. These operations are pure at the effect level: they add no
capability to `Φ`.

### 3.7 Capabilities (§4.13 – §4.14) — the core of E2

This is the rule that makes the system non-trivial.

```
    (extern defun f (x̄ : τ̄) : τ @capability c) ∈ Γ
    Γ ⊢ eᵢ : τᵢ ! Φᵢ
    ───────────────────────────────────────────────────  [T-extern-call]
              Γ ⊢ (f ē) : τ ! {c} ∪ ⋃ᵢ Φᵢ

    ──────────────────────────────────────────────────────  [T-extern-decl]
    Γ ⊢ (extern defun f (x̄ : τ̄) : τ @capability c) : Unit ! ∅
    Γ' = Γ, f : (τ̄) → τ ! {c}   (in continuation)
```

Every call to a capability-annotated external function contributes its
capability name to the effect. Declaration is pure; invocation is not.

**Mocks.** The mock frame (E1 §3, §4.14) is a *runtime* concept; the
effect system tracks the capability independently of whether a mock
shadows the real implementation. This is intentional: the checker's
reference interpreter uses mocks, but the binary in production does
not. Tracking `{c} ∈ Φ` on every extern call regardless of mock state
keeps the typing judgment faithful to the production binary's effects.

### 3.8 Assertions and contracts (§4.15 – §4.17)

```
    Γ ⊢ eᵢ : τ ! Φᵢ   (τ is any comparable type)
    ───────────────────────────────────────────────  [T-assert-eq]
        Γ ⊢ (assert-eq e₁ e₂) : Unit ! Φ₁ ∪ Φ₂

    Γ ⊢ e : Bool ! Φ
    ─────────────────────────────────  [T-require]  /  [T-ensure]
    Γ ⊢ (require e) : Unit ! Φ
    Γ ⊢ (ensure  e) : Unit ! Φ
```

Assertions contribute no new capability; they inherit effects from
their operand sub-expressions.

### 3.9 Tests (§4.16)

```
    Γ ⊢ eᵢ : τᵢ ! Φᵢ   (for each i)
    ─────────────────────────────────────────────  [T-test]
     Γ ⊢ (test t e₁ … eₙ) : Unit ! ⋃ᵢ Φᵢ
```

A test's effect is the union of its body's effects. If the manifest
declares `Φ_declared` and `T-test` infers `Φᵢ` for some `tᵢ`, the
checker rejects unless `Φᵢ ⊆ Φ_declared`. This is the definition of
**capability soundness (CS)** in `safety-policy.md` §3.1.

---

## 4 · Meta-theorems

Stated here; proofs sketched in `docs/soundness.md` (E3).

### 4.1 Progress

If `· ⊢ e : τ ! Φ` and `σ.μ` covers every extern call reachable from
`e`, then either `e` is a value or there exist `e', σ'` with
`e, σ ⟶ e', σ'` (E1 §4).

### 4.2 Preservation

If `Γ ⊢ e : τ ! Φ` and `e, σ ⟶ e', σ'`, then `Γ ⊢ e' : τ ! Φ'` with
`Φ' ⊆ Φ`.

### 4.3 Effect monotonicity

Corollary of 4.2 (and a stronger version of it): effects shrink under
reduction. No reduction step introduces a capability that was not
already in the pre-state's effect set.

### 4.4 Effect soundness

If `· ⊢ e : τ ! Φ` and `e, σ ⟶* v, σ'`, then every capability `c`
invoked during the reduction sequence satisfies `c ∈ Φ`. Equivalently:
`σ'.φ ⊆ σ.φ ∪ Φ` after execution (where `σ.φ` is E1's declared-cap
component).

This is the formal sibling of `CapabilityExtractor`'s correctness
premise (TA-1 in `docs/safety-policy.md`): the syntactic effect `Φ` is
a sound over-approximation of the dynamic capability set.

---

## 5 · Inference

E2 is decidable and is implemented by a simple bottom-up pass on the
AST:

1. For each top-level `(extern defun f … @cap c)`, bind
   `f ↦ (τ̄) → τ ! {c}` in the initial `Γ`.
2. For each `(defun f (x̄ : τ̄) : τ e)`, infer `e` in `Γ, x̄ : τ̄`;
   record the resulting `Φf`; bind `f ↦ (τ̄) → τ ! Φf` in `Γ`.
3. For each `(test t …)`, infer the body; emit `(test-effect t Φt)`.
4. The set `⋃ₜ Φt` is the **total test-observable effect**. The
   transpiler compares this against `manifest.Capabilities`.

Inference is linear in AST size × `|Cap|` (a finite constant).

*The current transpiler performs steps 1–3 informally — the manifest's
`Capabilities` field today lists the capabilities the binary actually
uses at the syntactic-presence level (see `CapabilityExtractor`). A
mechanized pass that also binds `f ↦ Φf` for every user function is
deferred to E4 (post-paper).*

---

## 6 · Extensions (future work)

### 6.1 E2.1 — Higher-order functions

First-class functions require the arrow type `(τ̄) → τ ! Φ` to be a
first-class value, propagated through `arr.map`, `arr.filter`,
`arr.reduce`, and `lambda`. The rule shape is:

```
    Γ, x̄ : τ̄ ⊢ e : τ ! Φ
    ────────────────────────────────────────  [T-lambda]
    Γ ⊢ (lambda (x̄ : τ̄) e) : (τ̄) → τ ! Φ ! ∅

    Γ ⊢ f : (τ̄) → τ ! Φf       Γ ⊢ eᵢ : τᵢ ! Φᵢ
    ────────────────────────────────────────────────  [T-apply]
          Γ ⊢ (f ē) : τ ! Φf ∪ ⋃ᵢ Φᵢ
```

Note the double `!` in `T-lambda`: the *latent* effect `Φ` on the
arrow, and the *immediate* effect `∅` of the closure allocation.
`arr.map : (Array α) × ((α) → β ! Ψ) → Array β ! Ψ` propagates the
callback's latent effect. Deferred to E4.

### 6.2 E2.2 — Modules and imports

Modules contribute a signature `Σ_m = Γm × Φm_trans` — the exported
bindings and the transitive capability closure of the module body. An
`(import std.foo)` form extends the importing module's `Γ` with
`Σ_foo.Γ`; capability aggregation for the top-level program becomes
`⋃ Σ_m.Φ_trans ∪ Φ_main`. Deferred to E4.

### 6.3 E2.3 — Refinement effects

Finer-grained effects (e.g., `file.read[path=/etc/*]`,
`http.fetch[host=api.example.com]`) require a dependent-effect
extension. Useful for the auditability story but out of scope for the
first paper.

---

## 7 · Relation to the checker

`Agentic.Check` does not implement the type-and-effect judgment
explicitly — it operates at the substring / reduction level. E2's role
is **specification**, not algorithm:

| Arc           | Consumes E2                                                        |
|---------------|--------------------------------------------------------------------|
| C5 (policy)   | CS is stated as: `Φ_observed ⊆ Φ_declared`, where `Φ_observed` is §4.4 applied to each test. |
| C7 (checker)  | `CapabilityExtractor` is a syntactic under-approximation of §4.4. A sound (proof) version of the extractor would replay this judgment. |
| E3 (soundness)| Theorem **TH-CS** reduces to §4.4 + `CapabilityExtractor` correctness (TA-1). |

In other words: E2 is the formal contract; C5–C7 are engineering
realizations of it. A future mechanized E4 would close the last gap.

---

## 8 · References

- `docs/semantics.md` §2, §4, §7 — the reduction relation this effect
  system layers on top of.
- `docs/safety-policy.md` §3.1 — CS stated as a property of `Φ`.
- Gifford, Lucassen. *Integrating functional and imperative
  programming*. LFP '86. — the standard source for effect systems.
- Nielson, Nielson, Hankin. *Principles of Program Analysis*, Ch. 5.
- ROADMAP.md Arc E — context for E1/E2/E3/E4 staging.
