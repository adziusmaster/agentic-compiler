## Invoice total

Compute an invoice total. Given a per-item `unit_price` and `quantity`,
a `discount_pct` (0–100), a `tax_pct` (0–100), and a fixed `shipping`
amount, return the final total rounded to 2 decimals.

Formula:
```
subtotal  = unit_price * quantity
discount  = subtotal * discount_pct / 100
taxed     = (subtotal - discount) * (1 + tax_pct / 100)
total     = taxed + shipping     (rounded to 2 decimals)
```

- AGC signature: `(defun invoice_total ((unit_price : Num) (quantity : Num) (discount_pct : Num) (tax_pct : Num) (shipping : Num)) : Num)`.
- Python signature: `invoice_total(unit_price, quantity, discount_pct, tax_pct, shipping) -> float`.
- Round using banker's/standard rounding; tests tolerate ±0.01.
- Decompose into helpers if it helps clarity.
