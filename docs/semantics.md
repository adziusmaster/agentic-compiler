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

### 4.10 Arrays (`arr.*`)

Arrays are total finite tuples `⟨v₀, …, vₙ₋₁⟩`. Indexing is zero-based.
Out-of-bounds access is **stuck** (checker rejects).

```
    n ∈ ℕ
────────────────────────────────────  [arr-new]
 (arr.new n), σ ⟶ ⟨0, 0, …, 0⟩_n, σ

  0 ≤ i < n    v = vᵢ                        0 ≤ i < n    v̄' = v̄[i := v']
──────────────────────────────────        ────────────────────────────────────────────  [arr-set]
 (arr.get ⟨v̄⟩ i), σ ⟶ v, σ                  (arr.set ⟨v̄⟩ i v'), σ ⟶ ⟨v̄'⟩, σ
 [arr-get]

        |v̄| = n
──────────────────────────────
 (arr.length ⟨v̄⟩), σ ⟶ n, σ
 [arr-len]
```

`arr.map`, `arr.filter`, and `arr.reduce` take a **named function
reference** as their second argument. Because they evaluate that
function at each element (i.e., higher-order application), they are
**out of scope for E1** (see §6.4). C8 refuses to emit VCs for any test
that contains a direct call to `arr.map` / `arr.filter` / `arr.reduce`
— the program still compiles and runs, but the checker's test-conformance
clause does not apply to those tests.

### 4.11 Maps (`map.*`)

Maps are total finite functions `{k̄ ↦ v̄}` with string keys.

```
──────────────────────────────────  [map-new]
 (map.new), σ ⟶ {}, σ

  m' = m ∪ {k ↦ v}                           m(k) = v                  k ∉ dom(m)
─────────────────────────────────          ────────────────────────   ──────────────────────────  [map-default]
 (map.set m k v), σ ⟶ m', σ                 (map.get m k), σ ⟶ v, σ    (map.get m k), σ ⟶ 0, σ
 [map-set]                                  [map-get-hit]

        k ∈ dom(m)                                   k ∉ dom(m)
────────────────────────────────                ────────────────────────────────
 (map.has m k), σ ⟶ 1, σ                        (map.has m k), σ ⟶ 0, σ
 [map-has-t]                                    [map-has-f]
```

`map.get` on a missing key returns `0` (the numeric zero). Programs
distinguishing "absent" from "present with value 0" should use
`map.has` first.

### 4.12 Capability call under mocks

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

### 4.13 Capability declaration (`extern defun`)

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

### 4.14 Mocks

A mocks clause registers entries in the mock frame for the duration of
the enclosing test.

```
  μ' = σ.μ ∪ {(cᵢ, kᵢ) ↦ vᵢ}
──────────────────────────────────────────────  [mocks]
 (mocks (c̄ k̄ v̄)), σ ⟶ 0, σ[μ := μ']
```

On exit from the enclosing `(test …)` block, the mock frame is popped
to its pre-test value. This is enforced by [test] below.

### 4.15 Assertions

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

### 4.16 Tests

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

### 4.17 Contracts

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
`strₛ` / `mathₛ` that wraps the corresponding host BCL function. These
functions are *pure* (no `σ` effect). The congruence rule is the same
for all of them:

```
       ops(v̄) = v
────────────────────────────  [stdlib-op]
 (op v̄), σ ⟶ v, σ
```

where `op ∈ dom(strₛ) ∪ dom(mathₛ)`. The metafunction tables below fix
`ops` concretely. An input outside the domain (wrong arity, wrong type)
is **stuck**; the checker logs a failure.

#### 5.1.1 `strₛ` — string metafunctions

| Operation          | Arity | Domain                   | Semantics                                                         |
|--------------------|-------|--------------------------|-------------------------------------------------------------------|
| `str.concat`       | 2     | (Str, Str) → Str         | `s₁ ++ s₂` (Unicode concatenation)                                |
| `str.length`       | 1     | Str → Num                | number of UTF-16 code units (host `s.Length`)                     |
| `str.trim`         | 1     | Str → Str                | remove leading + trailing whitespace (host `s.Trim()`)            |
| `str.to_num`       | 1     | Str → Num                | `double.Parse(s, CultureInfo.Invariant)`; **stuck** if it throws  |
| `str.from_num`     | 1     | Num → Str                | `n.ToString(CultureInfo.Invariant)`                               |
| `str.substring`    | 3     | (Str, Num, Num) → Str    | host `s.Substring(i, len)`; **stuck** if `i+len > |s|`            |
| `str.index_of`     | 2     | (Str, Str) → Num         | host `s.IndexOf(t)`, returns `-1` on miss                         |
| `str.eq`           | 2     | (Str, Str) → Num         | `1` if `s₁ = s₂` char-wise, else `0`                              |
| `str.upper`        | 1     | Str → Str                | host `s.ToUpperInvariant()`                                       |
| `str.lower`        | 1     | Str → Str                | host `s.ToLowerInvariant()`                                       |
| `str.starts_with`  | 2     | (Str, Str) → Num         | `1` if `s₁.StartsWith(s₂)`, else `0`                              |
| `str.ends_with`    | 2     | (Str, Str) → Num         | `1` if `s₁.EndsWith(s₂)`, else `0`                                |

Boolean-shaped results are returned as `0`/`1` to avoid a separate
Bool-vs-Num promotion rule; truthification (§4.4) coerces them back.

#### 5.1.2 `mathₛ` — numeric metafunctions

| Operation       | Arity | Domain                | Semantics                                                    |
|-----------------|-------|-----------------------|--------------------------------------------------------------|
| `math.abs`      | 1     | Num → Num             | `|n|`                                                        |
| `math.floor`    | 1     | Num → Num             | host `Math.Floor(n)` (IEEE-754)                              |
| `math.ceil`     | 1     | Num → Num             | host `Math.Ceiling(n)`                                       |
| `math.round`    | 1     | Num → Num             | host `Math.Round(n, MidpointRounding.ToEven)`                |
| `math.min`      | 2     | (Num, Num) → Num      | `min(n₁, n₂)` (NaN-propagating, per `Math.Min`)              |
| `math.max`      | 2     | (Num, Num) → Num      | `max(n₁, n₂)`                                                |
| `math.sqrt`     | 1     | Num → Num             | `Math.Sqrt(n)`; `NaN` for `n < 0`                            |
| `math.pow`      | 2     | (Num, Num) → Num      | `Math.Pow(b, e)`                                             |
| `math.mod`      | 2     | (Num, Num) → Num      | C# `%` on doubles; NaN if divisor is 0                       |

The `ops(v̄) = v` premise in [stdlib-op] is satisfied iff (a) the arity
matches, (b) each `vᵢ` has the declared domain type, and (c) the host
computation returns without throwing. If the host throws (e.g.,
`str.to_num "abc"`), the term is stuck.

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
- [x] Every construct used in `WeatherFetcher.ag`, `Calculator.ag`,
      `ShoppingCart.ag`, `Pipeline.ag`, and `samples/caps/*.ag` has a
      rule or is explicitly out of scope (see §8.1).
- [x] Independent reader (second pair of eyes) can trace one `(test …)`
      by hand in ≤ 10 minutes (see Appendix A).
- [ ] `ReferenceInterpreter.cs` stubs in `Agentic.Check/` compile with
      `// E1-rule-N` tags matching the numbering here. *(Week 4, C7
      project setup.)*

### 8.1 Construct coverage by sample

| Sample                         | Constructs used in tests                                                   | Rules / scope                                         |
|--------------------------------|----------------------------------------------------------------------------|-------------------------------------------------------|
| `Calculator.ag`                | `defun`, `call`, `bin-op`, `return`, `assert-eq`                           | §4.6, §4.7, §4.3, §4.8, §4.15                         |
| `ShoppingCart.ag`              | `defstruct`, `rec-new`, `rec-get`, `rec-set`, `call`, `bin-op`             | §4.9, §4.7, §4.3                                      |
| `WeatherFetcher.ag`            | `http.fetch` (extern), `mocks`, `str.substring`, `str.index_of`, `json.*`  | §4.13, §4.14, §5.1.1, §5.2 (`json.*` derived)         |
| `Pipeline.ag`                  | `arr.new`, `arr.set`, `arr.get`, `arr.length`                              | §4.10                                                 |
| `Pipeline.ag` *(HOF tests)*    | `arr.map`, `arr.reduce`, first-class function refs                         | **Out of scope for E1** (§4.10, §6.4 — deferred to E2)|
| `samples/caps/FileRead.ag`     | `file.read` (extern), `mocks`, `str.substring`, `str.index_of`             | §4.13, §4.14, §5.1.1                                  |
| `samples/caps/FileWrite.ag`    | `file.write` (extern), `mocks`, `str.concat`, `str.from_num`               | §4.13, §4.14, §5.1.1                                  |
| `samples/caps/EnvGet.ag`       | `env.get` (extern), `mocks`, `str.eq`, `if`                                | §4.13, §4.14, §5.1.1, §4.4                            |
| `samples/caps/DbQuery.ag`      | `db.query` (extern), `mocks`, `str.to_num`                                 | §4.13, §4.14, §5.1.1                                  |
| `samples/caps/ProcessSpawn.ag` | `process.spawn` (extern), `mocks`, `str.trim`, `str.concat`                | §4.13, §4.14, §5.1.1                                  |

Any test whose body invokes an out-of-scope construct is filtered by
C8 at manifest emission; the checker never sees those tests.

## 9. References

- IMPLEMENTATION_PLAN.md §Week 1–2.
- ROADMAP.md Arc E1.
- Standard small-step SOS presentation: Pierce, *Types and Programming
  Languages*, Ch. 3. Our judgment shape `e, σ ⟶ e', σ'` mirrors the
  imperative-fragment formulation there.

## Appendix A — Worked example

We trace Calculator's simplest test end-to-end. The goal is to show that
a second reader can follow the reduction rules without intuition —
every step cites a rule number from §4.

### Source

```
(module Calculator
  (defun add ((a : Num) (b : Num)) : Num
    (return (+ a b)))

  (test add
    (assert-eq (add 1 2) 3)))
```

### Initial state

After the parser has linked the module, the top-level reducer receives
the expression `(do (defun add …) (test add …))` in store

```
σ₀ = ⟨Γ₀ = ∅, μ₀ = ∅, ρ₀ = ∅, φ₀ = ∅, τ₀ = ε⟩
```

(no bindings, no mocks, no records declared, no capabilities, empty
test log).

### Step-by-step

1. **[ctx] + [defun].** The outer `do` evaluates its first subterm
   `(defun add ((a : Num) (b : Num)) : Num (return (+ a b)))` at the
   hole. [defun] registers the closure
   `cl_add = ⟨Γ₀, (a, b), (return (+ a b))⟩` in the environment, yielding
   store `σ₁ = σ₀[Γ.add := cl_add]` and reducing to `0`. [do-step] then
   advances the sequence.

2. **Enter test.** [ctx] drives reduction into `(test add …)`. The test
   rule opens a `τ-scope` snapshotting `μ₁ = μ₀ = ∅` and setting
   `current-test := add`. Store becomes
   `σ₂ = σ₁[current-test := add]`.

3. **[ctx] into the assertion.** Evaluation contexts reduce the inner
   call `(add 1 2)` to its value first. The arguments `1` and `2` are
   already values — rule [val] applies at each argument position.

4. **[call] on `add`.** With `σ₂.Γ(add) = cl_add = ⟨Γ₀, (a, b),
   (return (+ a b))⟩`, the rule pushes a new frame, yielding
   `σ₃ = σ₂[Γ := Γ₀, a := 1, b := 2]` and the redex becomes
   `(return (+ a b))`.

5. **[ctx] + [var].** Inside `(+ a b)`, [var] resolves `a ↦ 1` and
   `b ↦ 2`. The redex becomes `(+ 1 2)`.

6. **[bin-op] for `+`.** `1 +ₛ 2 = 3`, so `(+ 1 2), σ₃ ⟶ 3, σ₃`.

7. **[return].** `(return 3), σ₃ ⟶ 3, σ₄` where `σ₄` pops the frame —
   `σ₄.Γ = σ₂.Γ` (the caller's environment is restored).

8. **Back to the assertion.** The outer expression is now
   `(assert-eq 3 3)`. [val] has already reduced both arguments.

9. **[assert-eq-pass].** `3 = 3`, so `(assert-eq 3 3), σ₄ ⟶ 0, σ₄`.
   The test log is unchanged (no failure).

10. **[test-pass].** The test body `(assert-eq …)` has reduced to `0`
    and no failures were attributed to `add` during its execution.
    The rule appends `(add, pass)` to the log:
    `σ₅ = σ₄[τ := (add, pass)]`.

11. **[do-done].** The outer `do` has exhausted its subterms and
    reduces to `0`.

### Final state

```
σ₅ = ⟨Γ = {add ↦ cl_add}, μ = ∅, ρ = ∅, φ = ∅,
      τ = (add, pass)⟩
```

The test log `τ` is exactly the output the checker emits for this
manifest entry: `{"add": "pass"}`.

**Rule coverage:** [ctx], [defun], [do-step], [do-done], [val], [var],
[call], [bin-op], [return], [assert-eq-pass], [test-pass].

**Reader time target:** a motivated reader should be able to verify
this trace by cross-referencing §4 rule-by-rule in under 10 minutes.
If it takes longer, the issue is almost certainly a rule whose form
doesn't quite match the redex — file an issue against this document.
