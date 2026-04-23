from solution import rental_cost


def approx(a, b, tol=0.01):
    return abs(a - b) <= tol


def test_one_day():     assert approx(rental_cost(1, 50, 10, 0, 0), 60)
def test_week():        assert approx(rental_cost(7, 50, 10, 0, 0), 370)
def test_eight_days():  assert approx(rental_cost(8, 50, 10, 0, 0), 430)
def test_loyalty():     assert approx(rental_cost(7, 50, 10, 1, 0), 333)
def test_with_tax():    assert approx(rental_cost(7, 50, 10, 0, 10), 407)
def test_loyalty_tax(): assert approx(rental_cost(7, 50, 10, 1, 10), 366.3)
