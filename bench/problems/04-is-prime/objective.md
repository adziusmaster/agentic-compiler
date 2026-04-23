## Primality test

Write `is_prime(n: Num) -> Num` returning `1` if `n` is a prime
number, `0` otherwise.

- Assume `n ≥ 0` and integral.
- `0`, `1` → 0 (not prime).
- `2`, `3`, `5`, `7` → 1.
- `4`, `9`, `15`, `25` → 0.
- A divisor check up to `sqrt(n)` inclusive is sufficient.
