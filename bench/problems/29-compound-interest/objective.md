## Compound interest

Compute final balance on an account with compound interest:
```
r = annual_rate_pct / 100 / periods_per_year
n = periods_per_year * years
amount = principal * (1 + r)^n
```

Use `math.pow`. Round to 2 decimals.

Signatures:
- AGC: `(defun compound ((principal : Num) (annual_rate_pct : Num) (periods_per_year : Num) (years : Num)) : Num)`.
- Python: `compound(principal, annual_rate_pct, periods_per_year, years) -> float`.
