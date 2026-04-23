## Word count

Write a pure function `word_count(text: Str) -> Num` that returns the
number of whitespace-separated words in `text`.

- Empty string → 0.
- Leading / trailing whitespace does not produce phantom words.
- Consecutive whitespace collapses (does not double-count).
- The only whitespace characters in scope are ASCII space and tab
  (`\t`); do not handle Unicode.
