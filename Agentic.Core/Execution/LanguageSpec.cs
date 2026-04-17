namespace Agentic.Core.Execution;

/// <summary>
/// Formal specification of the Agentic language. Designed to fit in an LLM system
/// prompt (~2K tokens). This is the definitive reference for AI agents writing .ag code.
/// </summary>
public static class LanguageSpec
{
    /// <summary>
    /// Returns the complete Agentic language specification as a system prompt.
    /// </summary>
    public static string GetSpec() => Spec;

    private const string Spec = @"# Agentic Language Specification v2.0

## Syntax
S-expressions. Every construct is `(op args…)`. One root expression per file.

## Types
Num (double), Str (string), Bool (boolean), (Array Num), (Array Str), (Map Str Num), (Map Str Str).

## Module Structure
```
(module Name
  (import std.math)           ; load stdlib module
  (import std.string)
  (import std.bool)
  (import ""./utils.ag"")       ; import local file module
  (import ""./lib/helpers"")    ; .ag extension auto-added
  (export func1 func2)       ; public API — only these are visible to importers
  …body…)
```

### Multi-File Imports
- `(import ""./path.ag"")` loads another .ag module relative to the current file
- Only `(export ...)` symbols are callable from the importing module
- Non-exported functions are private — usable internally but hidden from importers
- Circular imports are detected and rejected
- Diamond imports (A->B->D, A->C->D) work correctly — D is loaded once

## Variable Binding
```
(def x 5)                  ; untyped, inferred
(def x : Num 5)            ; explicitly typed
(set x 10)                 ; mutate existing variable
```
`def` creates, `set` mutates. `set` on undeclared variable is an error.

## Functions
```
;; untyped (all params/return default to Num)
(defun add (a b) (return (+ a b)))

;; typed
(defun greet ((name : Str) (n : Num)) : Str
  (return (str.concat ""Hello "" name)))
```
Body is a single expression or multiple expressions (auto-wrapped in `do`).
`(return expr)` exits the function. Recursion allowed (depth cap: 192).

## Control Flow
```
(if cond then-expr else-expr)   ; else is optional
(while cond (do …body…))        ; loop body MUST be (do …)
(do expr1 expr2 … exprN)        ; sequence, returns last value
```

## Arithmetic & Comparison
Strictly binary: `(+ a b)`, `(- a b)`, `(* a b)`, `(/ a b)`.
For 3+ operands, nest: `(+ a (+ b c))`.
Comparisons: `(< a b)`, `(> a b)`, `(= a b)`, `(<= a b)`, `(>= a b)`.

## Boolean Operators
`(not x)`, `(and a b)`, `(or a b)`.

## Arrays
```
(def arr (arr.new 5))           ; allocate array of size 5
(arr.get arr 0)                 ; read element at index
(arr.set arr 0 42)              ; write element at index
(arr.length arr)                ; array length → Num
```
Arrays MUST be initialized with `arr.new` directly. Never `(def arr 0)`.

## Higher-Order Array Functions
```
(arr.map arr func_name)         ; apply func to each element → new array
(arr.filter arr func_name)      ; keep elements where func returns truthy → new array
(arr.reduce arr func_name init) ; fold left with binary func and accumulator → value
```
`func_name` is a named function defined with `defun`. No lambdas.
For map/filter: func takes one arg. For reduce: func takes (accumulator, element).

## Strings
```
(str.concat a b)                ; concatenate
(str.from_num n)                ; number → string
(str.to_num s)                  ; string → number
(str.length s)                  ; string length → Num
(str.eq a b)                    ; string equality → Bool
(str.contains s sub)            ; check substring → 1.0 or 0.0
(str.index_of s sub)            ; find position → Num (-1 if not found)
(str.substring s start len)     ; extract portion → Str
(str.trim s)                    ; strip whitespace → Str
(str.upper s)                   ; uppercase → Str
(str.lower s)                   ; lowercase → Str
(str.replace s old new)         ; substitute all → Str
(str.split s delim)             ; split → (Array Str)
(str.join arr delim)            ; join array → Str
```
String variables MUST be initialized with a string value, never a number.

## HashMap (Dictionary)
```
(def m (map.new))               ; create empty map
(map.set m ""key"" value)         ; set key-value pair
(map.get m ""key"")               ; get value (0 if missing)
(map.has m ""key"")               ; check key exists → 1.0 or 0.0
(map.remove m ""key"")            ; remove key → 1.0 or 0.0
(map.keys m)                    ; all keys → (Array Str)
(map.size m)                    ; number of entries → Num
```
Keys are always strings. Values can be Num or Str.
Explicit type annotation: `(def m : (Map Str Num) (map.new))`.

## Math Library (import std.math)
Unary: `math.sin`, `math.cos`, `math.abs`, `math.sqrt`, `math.floor`, `math.ceil`, `math.log`.
Binary: `math.pow`, `math.mod`, `math.min`, `math.max`.
Zero-arg: `(math.random)` — returns a random double in [0, 1).

## I/O
```
(sys.input.get idx)             ; read CLI arg as Num (0-indexed)
(sys.input.get_str idx)         ; read CLI arg as Str
(sys.stdout.write expr)         ; print to stdout (no newline)
```

## File I/O (import std.file) — requires --allow-file
```
(file.read path)                ; read file contents → Str
(file.write path content)       ; write string to file
(file.append path content)      ; append to file
(file.exists path)              ; check existence → 1.0 or 0.0
(file.delete path)              ; delete a file
```
During verification, file ops use an in-memory virtual filesystem.

## HTTP Client (import std.http) — requires --allow-http
```
(http.get url)                  ; HTTP GET → response body as Str
(http.post url body)            ; HTTP POST → response body as Str
```
HTTP ops are NOT available during verification (use mock data in tests).

## HTTP Server — requires --allow-http
```
(server.get ""/path/:param"" handler_fn)      ; register GET route (text response)
(server.post ""/path"" handler_fn)            ; register POST route (text response)
(server.json_get ""/path/:param"" handler_fn) ; register GET route (JSON response)
(server.json_post ""/path"" handler_fn)       ; register POST route (JSON response)
(server.listen 8080)                         ; start server on port
```
Route params use `:name` syntax. Handler functions are regular `defun` functions.
For GET routes, route params are passed as function arguments.
For POST routes, non-route `Str` params receive the request body.
JSON routes auto-set `Content-Type: application/json`.
Transpiles to ASP.NET Minimal API (WebApplication).
Tests still run at compile time. Only route registration + listen are server-specific.

## JSON (import std.json)
```
(json.get json_str key)         ; extract string value by key
(json.get_num json_str key)     ; extract numeric value by key
(json.object k1 v1 k2 v2 …)    ; build JSON object from key-value pairs
(json.array_length json_str)    ; count elements in JSON array → Num
```

## Environment Variables — requires --allow-env
```
(env.get ""KEY"")                 ; read env var (throws if missing)
(env.get_or ""KEY"" ""default"")    ; read env var with fallback
```
During verification, env.get returns """", env.get_or returns the default.

## Error Handling
```
(throw ""error message"")         ; raise an error
(try
    (risky_operation)
    (catch err
        (do …handle error…)))   ; err is bound to the error message string
```
`try/catch` catches thrown errors, contract violations, and runtime errors.
The catch variable receives the error message as a Str.

## Records (defstruct)
```
(defstruct Point (x y))        ; all fields Num
(def p (Point.new 3 4))        ; construct
(Point.x p)                    ; read field
(Point.set-x p 10)             ; wither → new record
```
Records are immutable. `set-*` returns a copy.

## Contracts
```
(require (> x 0))              ; precondition — aborts if false
(ensure (> result 0))          ; postcondition — aborts if false
```
Use inside function bodies. Emitted as runtime guards in the binary.

## Tests (compilation targets)
```
(test function-name
  (assert-eq (add 1 2) 3)              ; exact equality
  (assert-true (> x 0))                ; truthy check
  (assert-near 3.14 pi 0.01))          ; float tolerance
```
Tests run during compilation. Failure = build failure. Not emitted to binary.

## Compiler Output
Success: `(ok (binary ""path"") (tests-passed N/M))`
Failure: `(error (diagnostic (type ""…"") (message ""…"") (fix-hint ""…"")))`

## Rules
1. Root must be `(module …)` or `(do …)`.
2. No comments (no `;`).
3. Math is strictly binary — always nest.
4. `while` body must be `(do …)`.
5. `defun`, `defstruct` at module top level.
6. `return` only inside `defun`, never at root.
7. Array/string vars initialized with correct type from the start.
8. `arr.map`/`arr.filter`/`arr.reduce` take named function references, not lambdas.
";
}
