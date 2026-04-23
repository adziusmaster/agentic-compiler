## Port number from environment

Read env var `PORT` and return it parsed as a number. Return `default`
if the variable is empty or unset.

- AGC: declare `(extern defun env_read ((key : Str)) : Str @capability "env.get")`
  and implement `port_or(default: Num) -> Num`.
- Python: `port_or(default: int, env_get)` where `env_get(key) -> str`.
- Use `str.to_num` (AGC) / `int(...)` (Python) for conversion. Assume
  the env variable, when set, contains a valid integer.
