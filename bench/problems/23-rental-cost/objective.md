## Car rental cost

Compute rental cost for a given number of `days` at `daily_rate`.

Rules:
- Weekly pricing: every full 7 days costs 6×daily_rate (one day free).
- Remainder days charged at daily_rate.
- `insurance_per_day` is added per actual day, including weekly days.
- If `loyalty` is 1, apply 10% off the total (before tax).
- Tax of `tax_pct`% is added at the end.
- Round to 2 decimals.

Signatures:
- AGC: `(defun rental_cost ((days : Num) (daily_rate : Num) (insurance_per_day : Num) (loyalty : Num) (tax_pct : Num)) : Num)`.
- Python: `rental_cost(days, daily_rate, insurance_per_day, loyalty, tax_pct) -> float`.
