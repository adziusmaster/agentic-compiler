from solution import compound


def approx(a, b, tol=0.05):
    return abs(a - b) <= tol


def test_annual():     assert approx(compound(1000, 5, 1, 10), 1628.89)
def test_monthly():    assert approx(compound(1000, 10, 12, 5), 1645.31)
def test_zero_rate():  assert approx(compound(5000, 0, 4, 10), 5000)
def test_double():     assert approx(compound(100, 100, 1, 1), 200)
def test_semiannual(): assert approx(compound(2000, 6, 2, 3), 2388.10)
