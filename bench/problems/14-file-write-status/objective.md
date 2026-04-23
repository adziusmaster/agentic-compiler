## Write a status file

Write a string like `"OK count=<n>"` to a path and return the write
result code (`1` on success in our capability contract).

- AGC: declare `(extern defun file_write ((path : Str) (body : Str)) : Num @capability "file.write")`
  and implement `save_status(path: Str, count: Num) -> Num`.
- Python: `save_status(path: str, count: int, write_text) -> int` where
  `write_text(path, body) -> int` is injected.
- The message format is literally `"OK count=" + <decimal n>`.
