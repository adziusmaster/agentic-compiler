## Count rows via SQL scalar

Run a scalar SQL query via a DB capability and return the result as a number.

- AGC: `(extern defun sql_scalar ((conn : Str) (sql : Str)) : Str @capability "db.query")`,
  implement `count_users(conn: Str) -> Num`. Query text is
  exactly `"SELECT COUNT(*) FROM users"`.
- Python: `count_users(conn: str, sql_scalar)` where
  `sql_scalar(conn, sql) -> str`.
- Return type is a number.
