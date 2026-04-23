## Count character

Write `count_char(s: Str, ch: Str) -> Num` returning the number of
occurrences of the single character `ch` in `s`.

- `ch` is always exactly one character in length.
- Case-sensitive: `count_char("Banana", "a") = 3` (lowercase), `count_char("Banana", "A") = 0` (uppercase A never appears).
- `count_char("", "a") = 0`.
- `count_char("aaaa", "a") = 4`.
