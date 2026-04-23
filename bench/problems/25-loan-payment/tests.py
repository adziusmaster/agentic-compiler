from solution import monthly_payment


def approx(a, b, tol=0.05):
    return abs(a - b) <= tol


def test_zero_rate():     assert approx(monthly_payment(12000, 0, 1), 1000)
def test_simple():        assert approx(monthly_payment(100000, 6, 30), 599.55)
def test_short_term():    assert approx(monthly_payment(10000, 5, 2), 438.71)
def test_high_interest(): assert approx(monthly_payment(5000, 20, 1), 463.17)
