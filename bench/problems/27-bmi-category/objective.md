## BMI category

Given weight in kg and height in cm, compute BMI = `kg / (m*m)` where `m = cm / 100`, and classify:
- BMI < 18.5  → `"underweight"`
- BMI < 25    → `"normal"`
- BMI < 30    → `"overweight"`
- otherwise   → `"obese"`

Signatures:
- AGC: `(defun bmi_category ((kg : Num) (cm : Num)) : Str)`.
- Python: `bmi_category(kg, cm) -> str`.
