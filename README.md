# Agentic Compiler

An AI-native compiler where LLMs write programs in a formal S-expression language, and a deterministic pipeline verifies, tests, and compiles them to **native binaries** via C#/AOT.

## How It Works

```
Intent → LLM generates .ag source → Parser → Verifier (runs tests) → Transpiler (→ C#) → Native AOT binary
```

1. **You describe** what you want in plain English
2. **An LLM agent** writes correct `.ag` source code
3. **The compiler** verifies it (runs all tests), transpiles to C#, and emits a native binary
4. If tests fail, the agent retries with structured error feedback (up to 3 attempts)

You can also write `.ag` files by hand and compile them directly — no LLM needed.

## Quick Start

```bash
# Build the compiler
dotnet build

# Compile a .ag file directly (no LLM)
cd Agentic.Cli
dotnet run -- compile samples/Calculator.ag

# Let the LLM agent write code from an intent
dotnet run -- agent "Build a function that calculates the average of 4 numbers" --out Average

# Check syntax and run tests only (no binary)
dotnet run -- check samples/Calculator.ag

# Compile a multi-file project directory
dotnet run -- compile samples/project/
```

## The Agentic Language

A statically-typed S-expression language designed for AI agents to write correctly.

### Module Structure
```lisp
(module Calculator
  (import std.math)              ; stdlib (always available)
  (import "./math_lib.ag")       ; import local module
  (export add multiply)          ; public API

  (defun add ((a : Num) (b : Num)) : Num
    (return (+ a b)))

  (defun multiply ((a : Num) (b : Num)) : Num
    (return (* a b)))

  (test add
    (assert-eq (add 1 2) 3)
    (assert-eq (add 0 0) 0))

  (sys.stdout.write (add 10 20)))
```

### Types
| Type | Description |
|------|-------------|
| `Num` | 64-bit float (double) |
| `Str` | String |
| `Bool` | Boolean |
| `(Array Num)` | Numeric array |
| `(Array Str)` | String array |
| `(Map Str Num)` | String→Number hashmap |
| `(Map Str Str)` | String→String hashmap |

### Core Features
```lisp
; Variables
(def x : Num 42)
(set x (+ x 1))

; Functions
(defun greet ((name : Str)) : Str
  (return (str.concat "Hello, " name)))

; Control flow
(if (> x 0) (sys.stdout.write "positive") (sys.stdout.write "non-positive"))
(while (< i 10) (do (set i (+ i 1))))

; Arrays
(def nums : (Array Num) (arr.new 5))
(arr.set nums 0 42)
(arr.get nums 0)
(arr.map nums double_it)
(arr.filter nums is_positive)
(arr.reduce nums sum_fn 0)

; HashMaps
(def m : (Map Str Num) (map.new))
(map.set m "key" 100)
(map.get m "key")

; Error handling
(try (risky_call) (catch err (sys.stdout.write err)))

; Contracts
(require (> x 0))     ; precondition
(ensure (> result 0))  ; postcondition

; Records
(defstruct Point (x y))
(def p (Point.new 3 4))
(Point.x p)
```

### Multi-File Imports
```lisp
; math_lib.ag
(module MathLib
  (export add multiply)
  (defun add ((a : Num) (b : Num)) : Num (return (+ a b)))
  (defun multiply ((a : Num) (b : Num)) : Num (return (* a b)))
  (defun internal_helper ((x : Num)) : Num (return (* x x))))  ; private

; main.ag
(module Main
  (import "./math_lib.ag")
  (sys.stdout.write (add 10 20)))  ; ✓ add is exported
  ; (internal_helper 5)            ; ✗ would fail — not exported
```

- Only `(export ...)` symbols are visible to importers
- Circular imports are detected and rejected
- Diamond imports (A→B→D, A→C→D) work correctly

### Testing
Tests run **during compilation**. Failing tests = failed build.
```lisp
(test function_name
  (assert-eq (add 1 2) 3)           ; exact equality
  (assert-true (> x 0))             ; truthy check
  (assert-near 3.14 pi 0.01))       ; float tolerance
```

### HTTP Servers
```lisp
(module Api
  (defun hello ((name : Str)) : Str
    (return (str.concat "Hello, " name)))

  (server.get "/hello/:name" hello)
  (server.listen 8080))
```
Compile with `--allow-http` to enable network features.

### Capability FFI + Proof-Carrying Binaries
Any real I/O (HTTP, time, file, db) must be declared as a **trusted capability** via `extern defun`. The verifier refuses to run capability calls during tests unless a `(mocks …)` clause supplies a deterministic response. The compiled binary embeds a **proof manifest** — source hash, declared capabilities, granted permissions, and every test with its expected pass count.

```lisp
(module WeatherFetcher
  (extern defun http-fetch ((url : Str)) : Str @capability "http.fetch")

  (defun extract-temp ((body : Str)) : Num
    (return (str.to_num (str.substring body
      (+ (str.index_of body "temp") 7) 4))))

  (test fetches-and-parses
    (mocks (http-fetch "https://api.example.com/weather"
                       "{\"temp\": 21.5, \"city\": \"Amsterdam\"}"))
    (assert-eq (extract-temp (http-fetch "https://api.example.com/weather")) 21.5))

  (sys.stdout.write (extract-temp (http-fetch "https://api.example.com/weather"))))
```

```bash
agc compile samples/WeatherFetcher.ag --allow-http    # compile (tests-passed 1/1)
agc verify  ./WeatherFetcher                          # human-readable manifest
agc inspect ./WeatherFetcher                          # raw JSON manifest
```

The `agc verify` output:
```
Agentic binary manifest (schema 1.0)
  Source hash  : 5c0c12908c3b7fd6…
  Capabilities : http.fetch
  Permissions  : http
  Tests        : 1 (compile-time passes: 1)
    - fetches-and-parses
  Contracts    : 0
```

## CLI Reference

```
agc compile <file.ag|dir/>        Compile to native binary
agc check <file.ag|dir/>          Type-check and run tests only
agc verify <binary>               Extract & print embedded proof manifest
agc inspect <binary>              Dump the raw manifest JSON
agc agent <file.ag>               LLM-assisted compilation from constraint spec
agc agent "build me a ..."        Intent-driven: LLM writes .ag, compiler verifies

Options:
  --out <Name>                    Output filename (agent mode)
  --json                          JSON output instead of S-expr
  --check (agent only)            Verify without emitting binary
  --allow-file                    Allow file I/O operations
  --allow-http                    Allow HTTP/server operations
  --allow-env                     Allow reading environment variables
  --allow-db                      Allow SQLite database operations
  --allow-time                    Allow reading the system clock
  --allow-process                 Allow spawning subprocesses
```

## Project Structure

```
Agentic.Core/          Core compiler library
  Execution/           Compiler, Verifier, Transpiler, ModuleLoader
  Syntax/              Lexer, Parser, AST nodes
  Stdlib/              Standard library modules (math, string, file, http, etc.)
  Agent/               LLM agent workflow and client
Agentic.Cli/           CLI entry point
  samples/             Example .ag programs
Agentic.Core.Tests/    Test suite (272 tests)
```

## Requirements

- .NET 8.0 SDK
- LLM provider key (for agent mode only) — one of:
  - `ANTHROPIC_API_KEY` (Claude Sonnet 4.6 by default; override with `ANTHROPIC_MODEL`)
  - `OPENAI_API_KEY` (GPT-4o by default; override with `OPENAI_MODEL`)
  - `GEMINI_API_KEY` (Gemini 2.5 Flash)
- Force a provider with `AGENTIC_PROVIDER=anthropic|openai|gemini`; otherwise the first key present is used (Anthropic → OpenAI → Gemini).

## License

Proprietary — see [LICENSE](LICENSE).
