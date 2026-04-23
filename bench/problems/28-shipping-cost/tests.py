from solution import shipping_cost


def approx(a, b, tol=0.01):
    return abs(a - b) <= tol


def test_minimal():    assert approx(shipping_cost(0, 0, False), 5.00)
def test_zone_only():  assert approx(shipping_cost(0, 2, False), 10.00)
def test_weighted():   assert approx(shipping_cost(4, 0, False), 7.00)
def test_express():    assert approx(shipping_cost(0, 0, True), 20.00)
def test_combined():   assert approx(shipping_cost(10, 3, True), 35.00)
def test_fractional(): assert approx(shipping_cost(2.5, 1, False), 8.25)
