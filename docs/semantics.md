# E1 — Small-Step Operational Semantics for Core Agentic

**Status:** draft (Week 1 of IMPLEMENTATION_PLAN). Freeze target: Week 2.
**Scope:** the subset used by `(test …)` bodies and by `(require …)` /
`(ensure …)` contracts. This is the semantics the `agc-check` reference
interpreter (Arc C7) implements *literally*.

## 1. Why a reference semantics

Arc C delivers a separate checker (`agc-check`) that validates a compiled
binary against the manifest it carries. For the verdict to mean anything,
the checker must evaluate tests and contracts against a semantics that is:

1. **Independent of the compiler.** A bug in the transpiler must not be
   re-introduced by the checker. The checker's reference interpreter is
   written from this document, not from `Agentic.Core`.
2. **Small enough to read.** The entire reduction relation fits in this
   document. A reviewer can trace any `(test …)` execution by hand.
3. **Hermetic.** No clock, no network, no filesystem in the rules. I/O
   only enters through capability calls, and only as mocks under test
   semantics.

Out of scope for E1:

- Higher-order functions. `defun` is first-order; function references as
  values are semantically equivalent to named functions for our subset.
  E2 may extend.
- Modules and imports. The checker operates on already-linked programs —
  `ModuleLoader` runs at compile time, not at check time. Treat the
  incoming expression as a single tree.
- `try` / `catch`. Contracts abort; tests that would catch aborts are
  outside the VC subset.
- Async and concurrency. The language is sequential by design.

## 2. Syntax (core subset)

```
Type   τ  ::= Num | Str | Bool | Array τ | Map Str τ | Record ρ
Value  v  ::= n ∈ ℝ        numbers (IEEE-754 doubles)
            | s ∈ Σ*       strings (Unicode)
            | b ∈ {⊤, ⊥}   booleans
            | ⟨v̄⟩          arrays (ordered tuple of values)
            | {k̄ ↦ v̄}      maps (string-keyed total finite maps)
            | ρ{f̄ ↦ v̄}    records (nominal, by type name ρ)
            | ⟨λ, x̄, e⟩    closures (for first-class functions)

Expr   e  ::= v                                 literals
            | x                                 variable reference
            | (op e₁ e₂)                       arithmetic / comparison
            | (if e₁ e₂ e₃)                   conditional
            | (while e₁ (do ē))                loop
            | (do e₁ … eₙ)                    sequencing
            | (def x : τ e)                    binding
            | (set x e)                        mutation
            | (defun f (x̄ : τ̄) : τ e)         function definition
            | (defstruct ρ (f̄))                record type definition
            | (return e)                       function return
            | (f ē)                            function call
            | (c ē)                            capability call
            | (assert-eq e₁ e₂)                test assertion, exact
            | (assert-true e)                  test assertion, truthy
            | (assert-near e₁ e₂ e₃)           test assertion, tolerance
            | (require e)                      precondition
            | (ensure e)                       postcondition
            | (test t ē)                       test block
            | (mocks (c k v)…)                 mock declarations
            | (extern defun f (x̄:τ̄):τ @cap c)  capability declaration

Env    Γ  ::= variable → value  (lexically scoped)
Store  σ  ::= ⟨Γ, μ, ρ, φ, τ⟩  evaluation state (see §3)
```

Notation: `ē` abbreviates a sequence `e₁ … eₙ`; `x ↦ v` binds `x` to `v`;
`σ[x ↦ v]` denotes store update; `σ.Γ` projects the environment.

## 3. The store

The evaluation state `σ` is a five-tuple:

| Field | Meaning                                                     |
|-------|-------------------------------------------------------------|
| `Γ`   | variable environment (a stack of frames for function calls) |
| `μ`   | mock frame: partial function `(capname, key) → value`       |
| `ρ`   | record-type table: `ρname → (f̄)`                           |
| `φ`   | declared capability set (from `extern defun` declarations)  |
| `τ`   | test result log: sequence of `(name, pass|fail, msg?)`      |

Mocks and declared capabilities are **monotonic** within a single program
execution: they are only added, never removed. This simplifies progress
and preservation arguments in E2.

## 4. Small-step relation

The reduction relation is `e, σ ⟶ e', σ'`. Each rule has the form

```
    premise₁    premise₂    …
    ─────────────────────────    [name]
            conclusion
```

We use evaluation contexts in §4.1; all congruence rules are factored
through them. The non-trivial rules appear in §4.2 onwards.

### 4.1 Evaluation contexts

```
E ::= □
    | (op E e) | (op v E)
    | (if E e e) | (do E e… )
    | (def x : τ E) | (set x E)
    | (return E)
    | (f v̄ E ē) | (c v̄ E ē)
    | (assert-eq E e) | (assert-eq v E)
    | (assert-true E) | (assert-near v v E)
    | (require E) | (ensure E)
```

**Rule [ctx].** If `e, σ ⟶ e', σ'`, then `E[e], σ ⟶ E[e'], σ'`.

This reduces sub-expressions left-to-right. All remaining rules apply at
the redex.

### 4.2 Literals and variables

```
                                    σ.Γ(x) = v
──────────────  [val]             ─────────────────  [var]
 v, σ ⟶ v, σ                       x, σ ⟶ v, σ
```

### 4.3 Arithmetic and comparison

For each binary operator `⊙ ∈ {+, -, *, /, <, >, =, <=, >=}`:

```
v₁ ⊙ₛ v₂ = v
───────────────────────  [bin-op]
 (⊙ v₁ v₂), σ ⟶ v, σ
```

`⊙ₛ` is the semantic function. For numeric operators it is the
corresponding IEEE-754 double operation. For `=` on strings it is
character-wise equality; on booleans, Boolean equality. Division by zero
yields `NaN` (matching host semantics); programs that rely on this
behavior are encouraged to add `require` guards.

### 4.4 Conditional and sequencing

```
──────────────────────────────────  [if-true]
 (if ⊤ e₂ e₃), σ ⟶ e₂, σ

──────────────────────────────────  [if-false]
 (if ⊥ e₂ e₃), σ ⟶ e₃, σ

      ──────────────────────  [do-step]
       (do v e₂ …), σ ⟶ (do e₂ …), σ

            ──────────────  [do-done]
             (do v), σ ⟶ v, σ
```

Non-boolean condition values are truthified as in `IsTruthy`:
`0 ↦ ⊥, "" ↦ ⊥, null ↦ ⊥, everything-else ↦ ⊤`.

### 4.5 Loops

```
────────────────────────────────────────────────────────────────  [while]
 (while e₁ (do ē)), σ ⟶ (if e₁ (do ē (while e₁ (do ē))) 0), σ
```

`while` desugars to `if` with a recursive tail. Termination is not
guaranteed by the semantics; contracts and tests are expected to
supply reasoning when needed.

### 4.6 Binding and mutation

```
───────────────────────────────  [def]
 (def x : τ v), σ ⟶ v, σ[Γ.x := v]

       x ∈ dom(σ.Γ)
───────────────────────────────  [set]
 (set x v), σ ⟶ v, σ[Γ.x := v]
```

`set` on an undeclared variable is **stuck** (checker rejects). `def` on
a name already bound in the current scope is **stuck** (variables are
single-assignment at declaration time).

### 4.7 Function definition and first-order call

Function definitions register a closure in the environment:

```
  cl = ⟨σ.Γ, x̄, e⟩
──────────────────────────────────────────────  [defun]
 (defun f (x̄:τ̄):τ e), σ ⟶ 0, σ[Γ.f := cl]
```

Call reduction:

```
σ.Γ(f) = ⟨Γ₀, x̄, e⟩    σ' = σ[Γ := Γ₀, x̄ := v̄]
─────────────────────────────────────────────────────────────  [call]
 (f v̄), σ ⟶ e, σ'[Γ.frame-pushed := true]
```

The call pushes a new frame initialised with `Γ₀` extended by the
parameter bindings. On return (see [return]), the frame is popped and
execution resumes in the caller's frame.

Arity mismatches (`|v̄| ≠ |x̄|`) are **stuck** (checker rejects).

### 4.8 Return

```
                               σ' pops one frame from σ
───────────────────────────    ───────────────────────────  [return]
     (return v), σ ⟶ v, σ'
```

If `return` is reached outside a function frame the term is **stuck**
(checker rejects at top level; the parser rejects at module level).

### 4.9 Records

```
                                            σ.ρ(ρname) = (f̄)
──────────────────────────────────────      ─────────────────────────────────────────────  [rec-new]
 (defstruct ρname (f̄)), σ ⟶ 0,              (ρname.new v̄), σ ⟶ ρname{f̄ ↦ v̄}, σ
    σ[ρ.ρname := (f̄)]

        r = ρname{f̄ ↦ v̄}    fᵢ ∈ f̄                r = ρname{f̄ ↦ v̄}    fᵢ ∈ f̄
───────────────────────────────────────        ────────────────────────────────────────────────────────  [rec-set]
 (ρname.fᵢ r), σ ⟶ vᵢ, σ                       (ρname.set-fᵢ r v'), σ ⟶ ρname{f̄ ↦ v̄[fᵢ := v']}, σ
 [rec-get]
```

Records are immutable; `set-fᵢ` returns a new record with the field
replaced. Arity and type mismatches at `new` are **stuck**.

### 4.10 Capability call under mocks

This is the rule that makes test semantics hermetic.

```
σ.μ((c, k)) = v    k = v̄[0]     -- if c takes args, the first is the key
──────────────────────────────────────────────────  [cap-mocked]
 (c v̄), σ ⟶ v, σ
```

For zero-arg capabilities the key is `""`. For capabilities with no
exact mock, a wildcard entry `σ.μ((c, "*")) = v` may match; `cap-mocked`
applies either way.

If no mock matches, the expression is **stuck**. The checker treats
stuck-in-test as a test failure; the compiler's verifier raises
`OS Fault:`.

A second rule governs real I/O under the `AllowRealIo` policy, used
only outside tests in the verifier; the checker **never** applies it
(§6.3):

```
c ∈ σ.φ    real-adapter(c, v̄) = v    AllowRealIo = ⊤
────────────────────────────────────────────────────────  [cap-real]
 (c v̄), σ ⟶ v, σ
```

### 4.11 Capability declaration (`extern defun`)

```
   c ∈ registry-permissions     c ∉ σ.φ
──────────────────────────────────────────────────────  [extern-decl]
 (extern defun f (x̄:τ̄):τ @cap c), σ ⟶ 0,
    σ[φ := σ.φ ∪ {c},  Γ.f := ⟨ext, c⟩]
```

Declaration is a compile-time effect on the store; at runtime it desugars
to a binding from `f` to an extern-reference tagged with capability `c`.
Call sites for `f` reduce through [cap-mocked] / [cap-real] after
substituting `f` with `c`.

A second attempt to declare the same `c` is a no-op (monotonicity).
Declaring an `f` that collides with an in-scope variable is stuck.

### 4.12 Mocks

A mocks clause registers entries in the mock frame for the duration of
the enclosing test.

```
  μ' = σ.μ ∪ {(cᵢ, kᵢ) ↦ vᵢ}
──────────────────────────────────────────────  [mocks]
 (mocks (c̄ k̄ v̄)), σ ⟶ 0, σ[μ := μ']
```

On exit from the enclosing `(test …)` block, the mock frame is popped
to its pre-test value. This is enforced by [test] below.

### 4.13 Assertions

```
v₁ = v₂
───────────────────────────────────────────  [assert-eq-pass]
 (assert-eq v₁ v₂), σ ⟶ 0, σ

v₁ ≠ v₂
─────────────────────────────────────────────────────────────────────────────────  [assert-eq-fail]
 (assert-eq v₁ v₂), σ ⟶ 0, σ[τ := σ.τ · (current-test, fail, v₁ ≠ v₂)]

IsTruthy(v) = ⊤                               IsTruthy(v) = ⊥
────────────────────────────────────        ─────────────────────────────────────────  [assert-true]
 (assert-true v), σ ⟶ 0, σ                   (assert-true v), σ ⟶ 0, σ[τ := … fail …]

 |v₁ - v₂| ≤ v₃                              |v₁ - v₂| > v₃
────────────────────────────────────        ─────────────────────────────────────────  [assert-near]
 (assert-near v₁ v₂ v₃), σ ⟶ 0, σ            (assert-near …), σ ⟶ 0, σ[τ := … fail …]
```

Assertions never abort execution; they only log. The `(test …)` wrapper
turns a non-empty failure log within the block into a test failure.

### 4.14 Tests

```
   σ₀ = σ[τ-scope opened, current-test := t]    σ' = σ₀ after ē
   no new failure entries attributable to t during ē
───────────────────────────────────────────────────────────────────────────────  [test-pass]
 (test t ē), σ ⟶ 0, σ'[τ := σ'.τ · (t, pass)]

   σ' = σ₀ after ē                  failures ≥ 1 in t
───────────────────────────────────────────────────────────────────────────────  [test-fail]
 (test t ē), σ ⟶ 0, σ'[τ := σ'.τ · (t, fail)]
```

`τ-scope opened` snapshots `σ.μ`; on exit the mock frame is restored to
the snapshot. This is what makes tests independent: mocks installed in
one test do not leak into another.

### 4.15 Contracts

```
IsTruthy(v) = ⊤                               IsTruthy(v) = ⊥
──────────────────────────────             ───────────────────────────────  [require]
 (require v), σ ⟶ 0, σ                      (require v), σ ⟶ ⟦ABORT⟧, σ

IsTruthy(v) = ⊤                               IsTruthy(v) = ⊥
──────────────────────────────             ───────────────────────────────  [ensure]
 (ensure v), σ ⟶ 0, σ                       (ensure v), σ ⟶ ⟦ABORT⟧, σ
```

`⟦ABORT⟧` is a distinguished stuck state. The checker's contract-validity
clause rejects any program whose `(require …)` or `(ensure …)` reduces
to `⟦ABORT⟧` on *any* reachable path within the test's mock frame.

Inside a function, [ensure] is executed at every `(return …)` — the
transpiler inserts an `ensure` call; the semantics treats this as part
of the function's body.

## 5. Derived semantics of common forms

### 5.1 `str.*` and `math.*`

Each `str.X` and `math.X` operation is defined by a metafunction
`strₛ`/`mathₛ` that wraps the corresponding host BCL function. These
functions are *pure* (no `σ` effect) and *total* (they return for every
input in the type). The only impure path is `str.to_num` on a
non-numeric string, which follows C# `double.Parse` semantics and may
throw; the checker treats this as stuck (test fail).

### 5.2 `arr.*` and `map.*`

These are pure operations on `⟨v̄⟩` and `{k̄ ↦ v̄}` respectively. See
`semantics/arr.md` and `semantics/map.md` for full rules (extension
appendix; not required for E1 freeze).

## 6. What the checker implements

The reference interpreter in `Agentic.Check/ReferenceInterpreter.cs`
implements rules [ctx] through [ensure] literally. Each rule number
appears as a comment tag on the implementing code path. The interpreter
rejects any expression that does not match a rule (it does **not**
implement `try`/`catch`, `arr.map` with a closure argument, or
[cap-real]).

### 6.1 Inputs

- An AST snippet extracted from the manifest (VC form — see C8).
- The declared capability set `σ.φ` from the manifest.
- Mock bindings from the matching `(mocks …)` in the test's manifest
  entry.

### 6.2 Outputs

For each manifest test, one of:

- `(t, pass)` — the test reduced to `0` and its log has no failures.
- `(t, fail, reason)` — the log contains a failure (from §4.13) or the
  reduction got stuck.

For each contract (modeled as a degenerate test in C8):

- `(c, valid)` — every reachable `(require …)` / `(ensure …)` reduced
  to `0`.
- `(c, violated, reason)` — some reachable contract reduced to `⟦ABORT⟧`.

### 6.3 What it does *not* implement

- [cap-real]. Capability calls with no mock are stuck, always.
- `AllowRealIo`. The checker has no such flag and accepts no network /
  file / env / db / process actions.
- `try`/`catch`. Programs using `try` are outside the VC subset for E1;
  C8 refuses to emit VCs for tests that call `try`.

## 7. Meta-properties (to be proven in E3)

Stated here for reference; proofs live in `docs/soundness.md`.

- **Progress.** For every well-typed closed expression `e` (§E2) with
  store `σ` whose mocks cover every capability call `e` makes, either
  `e` is a value or `e, σ ⟶ e', σ'` for some `e', σ'`.
- **Preservation.** If `Γ ⊢ e : τ ! φ` and `e, σ ⟶ e', σ'`, then
  `Γ ⊢ e' : τ ! φ'` with `φ' ⊆ φ`.
- **Mock monotonicity.** The mock frame only grows within a `(test …)`
  block and is restored on exit.
- **Effect soundness.** The set of capabilities invoked during any
  reduction sequence is a subset of `σ.φ`.

## 8. Review checklist

Before this document is frozen (Week 2 exit):

- [x] Every rule the checker needs has a number.
- [ ] Every construct used in `WeatherFetcher.ag`, `Calculator.ag`,
      `ShoppingCart.ag`, `Pipeline.ag`, and `samples/caps/*.ag` has a
      rule or is explicitly out of scope.
- [ ] Independent reader (second pair of eyes) can trace one `(test …)`
      by hand in ≤ 10 minutes.
- [ ] `ReferenceInterpreter.cs` stubs in `Agentic.Check/` compile with
      `// E1-rule-N` tags matching the numbering here.

## 9. References

- IMPLEMENTATION_PLAN.md §Week 1–2.
- ROADMAP.md Arc E1.
- Standard small-step SOS presentation: Pierce, *Types and Programming
  Languages*, Ch. 3. Our judgment shape `e, σ ⟶ e', σ'` mirrors the
  imperative-fragment formulation there.
