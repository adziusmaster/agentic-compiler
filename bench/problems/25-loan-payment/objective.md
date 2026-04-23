## Monthly loan payment

Compute the fixed monthly payment for an amortizing loan.

Inputs: `principal`, `annual_rate_pct`, `years`.
Formula (standard amortization, use `math.pow`):
```
r = annual_rate_pct / 100 / 12
n = years * 12
payment = principal * r * (1+r)^n / ((1+r)^n - 1)
```
If `annual_rate_pct == 0`, payment = `principal / n`.

Round to 2 decimals.

Signatures:
- AGC: `(defun monthly_payment ((principal : Num) (annual_rate_pct : Num) (years : Num)) : Num)`.
- Python: `monthly_payment(principal, annual_rate_pct, years) -> float`.
