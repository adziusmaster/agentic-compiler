## First line of file

Read a text file and return its first line (everything up to the first
`\n`, or the whole contents if no newline).

- AGC: declare `(extern defun file_read ((path : Str)) : Str @capability "file.read")`
  and implement `first_line(path: Str) -> Str`.
- Python: `first_line(path: str, read_text)` where
  `read_text(path) -> str` is an injected reader. Do NOT call `open()` directly.
