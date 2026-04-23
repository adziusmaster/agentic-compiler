## Progressive tax

Compute tax on taxable income using three brackets:
- Bracket 1: first `$10,000` at 10%
- Bracket 2: next `$40,000` (income between 10,000 and 50,000) at 20%
- Bracket 3: everything above `$50,000` at 30%

For income `i`:
```
t1 = min(i, 10000) * 0.10
t2 = max(0, min(i, 50000) - 10000) * 0.20
t3 = max(0, i - 50000) * 0.30
tax = round2(t1 + t2 + t3)
```

Round to 2 decimals.

Signatures:
- AGC: `(defun income_tax ((income : Num)) : Num)`.
- Python: `income_tax(income) -> float`.
