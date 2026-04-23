## Copy one file to another

Read from `src`, write to `dst`, return the number of characters copied.

- AGC: declare TWO externs:
  `(extern defun file_read ((path : Str)) : Str @capability "file.read")` and
  `(extern defun file_write ((path : Str) (body : Str)) : Num @capability "file.write")`.
  Implement `copy_file(src: Str, dst: Str) -> Num`.
- Python: `copy_file(src: str, dst: str, read_text, write_text)` where
  `read_text(path) -> str` and `write_text(path, body) -> int`.
- If the source is empty, return 0 (and still call `write_text` with empty body).
