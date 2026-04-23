## Count lines in a file

Read a file and return the number of lines.

- A line is any run of characters ending in `\n`, plus an optional
  final non-empty trailing portion that lacks a newline.
- Empty file → 0.
- `"a\n"` → 1, `"a\nb\n"` → 2, `"a\nb"` → 2, `"\n"` → 1, `"\n\n"` → 2.
- AGC: `(extern defun file_read ((path : Str)) : Str @capability "file.read")`,
  implement `line_count(path: Str) -> Num`.
- Python: `line_count(path: str, read_text)` where `read_text(path) -> str`.
