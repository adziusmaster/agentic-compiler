from solution import net_pay


def approx(a, b, tol=0.05):
    return abs(a - b) <= tol


def test_no_overtime():   assert approx(net_pay(40, 20, 10, 5), 624.92)
def test_with_overtime(): assert approx(net_pay(50, 20, 10, 5), 859.27)
def test_zero_hours():    assert approx(net_pay(0, 20, 10, 5), 0)
def test_no_tax():        assert approx(net_pay(40, 10, 0, 0), 369.4)
