## Feature flag from env

Read env var `FEATURE_X` and return `1` if it is any of `"1"`, `"true"`,
`"yes"`, `"on"` (case-insensitive). Otherwise return `0`.

- Empty or unset → 0.
- Comparison is case-insensitive (`"TRUE"`, `"Yes"`, `"On"` all true).
- AGC: `(extern defun env_read ((key : Str)) : Str @capability "env.get")`,
  implement `is_enabled() -> Num`.
- Python: `is_enabled(env_get)` where `env_get(key) -> str`, returns `bool`.
