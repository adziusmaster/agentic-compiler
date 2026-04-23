from solution import income_tax


def approx(a, b, tol=0.01):
    return abs(a - b) <= tol


def test_zero():      assert approx(income_tax(0), 0)
def test_low():       assert approx(income_tax(5000), 500)
def test_boundary1(): assert approx(income_tax(10000), 1000)
def test_mid():       assert approx(income_tax(30000), 5000)
def test_boundary2(): assert approx(income_tax(50000), 9000)
def test_high():      assert approx(income_tax(100000), 24000)
