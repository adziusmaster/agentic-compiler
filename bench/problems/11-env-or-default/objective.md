## Environment variable with default

Read environment variable `HOME` and return it. If the variable is
missing or empty, return the provided fallback string.

- AGC: declare `(extern defun env_read ((key : Str)) : Str @capability "env.get")`
  and implement `home_or(fallback: Str) -> Str` that calls `env_read`.
- Python: signature `home_or(fallback: str, env_get)` where `env_get` is
  a callable `(key: str) -> str` returning `""` when absent. Do NOT
  call `os.environ` directly.
