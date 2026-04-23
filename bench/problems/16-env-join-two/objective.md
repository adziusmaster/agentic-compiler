## Join two env vars

Read env vars `DB_HOST` and `DB_PORT`, return `"<HOST>:<PORT>"`. If
either is empty, return `"localhost:5432"`.

- AGC: `(extern defun env_read ((key : Str)) : Str @capability "env.get")`,
  implement `db_url() -> Str`.
- Python: `db_url(env_get)` where `env_get(key) -> str`.
