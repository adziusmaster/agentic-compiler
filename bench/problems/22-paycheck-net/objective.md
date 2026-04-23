## Net paycheck

Given `hours`, `hourly_rate`, `federal_pct`, and `state_pct`, compute
take-home pay.

Rules:
- Overtime: hours above 40 earn 1.5× rate.
- FICA: flat 7.65% of gross.
- Federal tax: `federal_pct` of (gross – FICA).
- State tax: `state_pct` of gross.
- Net = gross – FICA – federal – state.

Signatures:
- AGC: `(defun net_pay ((hours : Num) (rate : Num) (fed_pct : Num) (state_pct : Num)) : Num)`.
- Python: `net_pay(hours, rate, fed_pct, state_pct) -> float`.
- Round to 2 decimals (tests tolerate ±0.01).
