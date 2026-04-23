## Tip and split

Compute per-person share of a restaurant bill.

Given `subtotal`, `tax_pct`, `tip_pct` (computed on pre-tax subtotal),
and `people`, return per-person cost, rounded up to the nearest cent
so everyone pays a whole-cent amount.

- Total = subtotal + tax + tip.
- Per-person total = ceil((total / people) × 100) / 100.
- `people ≥ 1`.

Signatures:
- AGC: `(defun per_person ((subtotal : Num) (tax_pct : Num) (tip_pct : Num) (people : Num)) : Num)`.
- Python: `per_person(subtotal, tax_pct, tip_pct, people) -> float`.
