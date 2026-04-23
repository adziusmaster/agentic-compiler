## Shipping cost

Compute total shipping cost:
```
base            = 5.00
weight_fee      = weight_lbs * 0.50
zone_fee        = [0, 2, 5, 10] indexed by zone (0..3)
express_fee     = 15 if express else 0
total           = round2(base + weight_fee + zone_fee + express_fee)
```

Signatures:
- AGC: `(defun shipping_cost ((weight_lbs : Num) (zone : Num) (express : Num)) : Num)` — `express` is 1/0.
- Python: `shipping_cost(weight_lbs, zone, express) -> float` — `express` is bool.
