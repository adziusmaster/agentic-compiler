from solution import per_person


def approx(a, b, tol=0.01):
    return abs(a - b) <= tol


def test_solo():       assert approx(per_person(100, 10, 20, 1), 130)
def test_duo():        assert approx(per_person(100, 10, 20, 2), 65)
def test_four():       assert approx(per_person(100, 10, 20, 4), 32.5)
def test_round_up():   assert approx(per_person(100, 10, 20, 3), 43.34)
def test_no_tip_tax(): assert approx(per_person(60, 0, 0, 4), 15)
