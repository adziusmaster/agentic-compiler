## Balanced parentheses

Write `is_balanced(s: Str) -> Num` that returns `1` if the parentheses
in `s` are balanced, `0` otherwise.

- Only `(` and `)` matter; other characters are ignored.
- Empty string → 1 (trivially balanced).
- Every `)` must have a preceding unmatched `(`.
- By the end, every `(` must be closed.
- `"()"` → 1, `"(()())"` → 1, `"(a(b)c)"` → 1, `")("` → 0, `"(("` → 0, `"())"` → 0.
