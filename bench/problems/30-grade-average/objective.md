## Grade average (drop lowest)

Given four numeric scores (0-100), drop the lowest and average the remaining three, rounded to two decimals, then return the letter grade:
- `avg >= 90` → `"A"`
- `avg >= 80` → `"B"`
- `avg >= 70` → `"C"`
- `avg >= 60` → `"D"`
- else        → `"F"`

Signatures:
- AGC: `(defun letter_grade ((s1 : Num) (s2 : Num) (s3 : Num) (s4 : Num)) : Str)`.
- Python: `letter_grade(s1, s2, s3, s4) -> str`.
