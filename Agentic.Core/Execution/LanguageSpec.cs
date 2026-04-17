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

    private const string Spec = @"# Agentic Language Specification v1.0

## Syntax
S-expressions. Every construct is `(op args…)`. One root expression per file.

## Types
Num (double), Str (string), Bool (boolean), (Array Num), (Array Str).

## Module Structure
```
(module Name
  (import std.math)        ; load stdlib module
  (import std.string)
  (import std.bool)
  (export func1 func2)     ; public API
  …body…)
```

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
Body is a single expression. Use `(do …)` for sequences.
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
```
Arrays MUST be initialized with `arr.new` directly. Never `(def arr 0)`.

## Strings
```
(str.concat a b)                ; concatenate
(str.from_num n)                ; number → string
(str.to_num s)                  ; string → number
(str.length s)                  ; string length → Num
(str.eq a b)                    ; string equality → Bool
```
String variables MUST be initialized with a string value, never a number.

## Math Library (import std.math)
Unary: `math.sin`, `math.cos`, `math.abs`, `math.sqrt`, `math.floor`, `math.ceil`, `math.log`.
Binary: `math.pow`, `math.mod`, `math.min`, `math.max`.

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

## HTTP (import std.http) — requires --allow-http
```
(http.get url)                  ; HTTP GET → response body as Str
(http.post url body)            ; HTTP POST → response body as Str
```
HTTP ops are NOT available during verification (use mock data in tests).

## JSON (import std.json)
```
(json.get json_str key)         ; extract string value by key
(json.get_num json_str key)     ; extract numeric value by key
(json.object k1 v1 k2 v2 …)    ; build JSON object from key-value pairs
(json.array_length json_str)    ; count elements in JSON array → Num
```

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
";
}
