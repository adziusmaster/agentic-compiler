# TCB — Trusted Computing Base of `agc-check`

**Version.** 1.0 (frozen 2026-04-21, Arc C9).
**Companion docs.** `docs/safety-policy.md` (what the checker promises),
`docs/semantics.md` (E1 — what the reference interpreter implements).

This document answers the reviewer's first question: *what do I have to
trust?* The answer, in order: (1) the code in `Agentic.Check/`, (2) the
BCL, (3) two named axioms. Nothing else in the repository is in the TCB.

---

## 1 · What is in the TCB

Exactly one project: `Agentic.Check/`. It compiles to a standalone
executable `agc-check`. `Agentic.Core` (the transpiler), `Agentic.Cli`
(the front-end), every sample, and every Anthropic / OpenAI / Gemini
client are **not** in the TCB — they are untrusted producers of the
subject triple `Π = (β, σ, μ)`. A reviewer verifying a binary runs
`agc-check` and ignores everything else.

### 1.1 Line count (hard budget: 1500)

| File                            | LOC |
|---------------------------------|----:|
| `Parser.cs`                     | 146 |
| `Manifest.cs`                   |  72 |
| `ReferenceInterpreter.cs`       | 597 |
| `CapabilityExtractor.cs`        |  63 |
| `Checker.cs`                    | 203 |
| `Program.cs`                    |  72 |
| **Total**                       | **1153** |

A CI gate in `Agentic.Check.csproj` fails the build if the total exceeds
1500 (see §5).

### 1.2 External dependencies (hard budget: BCL only)

- **NuGet packages:** *none.*
- **ProjectReferences:** *none.* (`Agentic.Check.csproj` contains no
  `<ProjectReference>` element.)
- **BCL namespaces used:**
  `System`, `System.Collections.Generic`, `System.Globalization`,
  `System.IO`, `System.Linq`, `System.Security.Cryptography`,
  `System.Text`, `System.Text.Json`, `System.Text.Json.Serialization`.

Any new NuGet dependency — including first-party Microsoft packages —
enlarges the TCB and must be justified in the same commit as the code
that needs it.

### 1.3 I/O performed by the checker itself

The checker is a read-only, network-silent program. Every syscall it
issues comes from this enumerated list:

| Operation                         | API used                       | Purpose                               |
|-----------------------------------|--------------------------------|---------------------------------------|
| File existence probe              | `File.Exists`                  | argv validation, sidecar lookup       |
| Read binary for SHA256            | `File.OpenRead`                | WF3 (`SHA256(β)`)                     |
| Read binary bytes for CS scan     | `File.ReadAllBytes`            | `CapabilityExtractor.Extract`         |
| Read sidecar JSON                 | `File.ReadAllText`             | `ManifestLoader.FromSidecar`          |
| Read `.ag` source (optional)      | `File.ReadAllText`             | WF2 (`SHA256(σ)`), `--source` mode    |
| Write to stdout / stderr          | `Console.Out`, `Console.Error` | verdict + diagnostics only            |

**Not used:** `File.Write*`, `File.Delete`, `Directory.Create*`,
`Process.Start`, `Socket`, `HttpClient`, `Environment.GetEnvironmentVariable`,
`Registry.*`. A reviewer can `grep` the project for each of those and
find zero hits.

---

## 2 · Trust assumptions (axioms)

The soundness of `agc-check`'s verdict rests on two named assumptions,
both stated in `docs/safety-policy.md`:

- **TA-1 — CapabilityExtractor soundness.** For every capability `c`
  registered in `Capability.DefaultCapabilities.BuildTrusted()`, the
  emitter's `CSharpEmitExpr` produces output containing at least one of
  the substrings listed in `Agentic.Check/CapabilityExtractor.cs`. If
  this holds, the extractor is a sound over-approximation of
  `syscalls(β)`: any capability actually invoked by the binary is
  detected. Violations are caught in review, not at runtime — every
  entry in the pattern table is cross-reviewed when a capability is
  added.
- **TA-2 — Emitter implements E1 on the test subset.** For each `(test
  …)` body and the `defun`s it transitively calls, the emitter
  translates the AST to C# whose runtime behaviour matches the
  reduction relation `⟶*_E1` defined in `docs/semantics.md` §4. The
  paper's Arc E3 (`docs/soundness.md`, pending) makes this a theorem
  under the E2 type-and-effect system; today it is an audit obligation
  on the transpiler.

These two axioms — and only these two — are where the "trusted"
adjective lives. Every other claim is mechanically re-verified from
`(β, σ, μ)`.

---

## 3 · Attack surface

What can a malicious actor do against the checker? The adversary
controls some prefix of the pipeline; we enumerate by position.

| Adversary controls | Can make checker…                | Cannot make checker…                  |
|--------------------|----------------------------------|---------------------------------------|
| Source `σ`         | accept any `.ag` that passes TC  | accept when `SHA256(σ)` ≠ declared    |
| Transpiler         | emit any `β` and any `μ`         | bypass CS — caps scanned from `β`     |
| Binary `β`         | carry arbitrary bytes            | evade capability substring scan (TA-1)|
| Manifest `μ`       | declare any capability set       | claim a passing test that ⟶*_E1 fails |
| CLI flags          | choose `safety` vs `strict`      | lower guarantees below `safety`       |
| Filesystem         | delete sidecar, flip bytes       | produce a spurious `accept`           |

A byte flip in the binary fails WF3. A byte flip in the sidecar fails
JSON parse or re-hashing. A forged `μ` that drops a declared capability
fails CS when the extractor re-scans `β`. A test that passes under the
emitter but fails under the reference semantics fails TC — this is the
entire point of the checker.

**Out of scope for this TCB:** side channels, resource exhaustion, and
the integrity of the compiler that produced `agc-check` itself. These
are declared as non-goals NG-4 through NG-7 in `docs/safety-policy.md`.

---

## 4 · What is *not* in the TCB

A reviewer should not read the following to understand the verdict:

- `Agentic.Core/` — the transpiler. Its bugs cannot produce a false
  `accept`; they can only produce `reject`s or unbuildable programs.
- `Agentic.Cli/` — the front-end. Only invokes the compiler; its
  diagnostic formatting does not feed into the verdict.
- Any NuGet package. The checker links none.
- The LLM provider (`AnthropicClient`, `OpenAiClient`, `AgentClient`).
  Even an adversarial LLM can only suggest `.ag` source — the same
  subject-triple pipeline runs over whatever it produces.
- The test suites `Agentic.Core.Tests`, `Agentic.Check.Tests`. They
  witness behaviour but are not themselves trusted.

---

## 5 · CI gate

`Agentic.Check.csproj` has a pre-build MSBuild target `CheckTcbLocBudget`
that counts non-blank lines across `Agentic.Check/*.cs` and fails with a
`TCB_LOC_BUDGET_EXCEEDED` error if the total exceeds **1500**. The
budget is deliberately tight; expanding it requires editing both the
csproj and this document.

---

## 6 · How to re-audit

1. `wc -l Agentic.Check/*.cs` — confirm total ≤ 1500.
2. `grep -R "PackageReference\|ProjectReference" Agentic.Check/` —
   confirm empty.
3. `grep -R "HttpClient\|Process.Start\|File.Write\|Socket\|Registry\|GetEnvironmentVariable" Agentic.Check/` —
   confirm empty.
4. Read `Checker.Run` top-to-bottom. Every branch maps to a
   well-formedness / CS / TC / CV check from `safety-policy.md` §2–§3.
5. Read `ReferenceInterpreter.cs`; every `case` in `Eval` is tagged
   `// E1-rule §4.N` matching `semantics.md`.
6. Read `CapabilityExtractor.Patterns`. Cross-check each entry against
   `Agentic.Core/Capabilities/DefaultCapabilities.cs` — same substring
   must appear in the capability's `CSharpEmitExpr`.

Total audit time target: **30 minutes**. If it takes longer, the TCB
has grown without updating this document, which is itself a C9 bug.
