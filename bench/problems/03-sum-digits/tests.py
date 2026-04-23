from solution import sum_digits


def test_zero():    assert sum_digits(0) == 0
def test_single():  assert sum_digits(9) == 9
def test_two():     assert sum_digits(23) == 5
def test_three():   assert sum_digits(123) == 6
def test_zeros():   assert sum_digits(1000) == 1
def test_big():     assert sum_digits(99999) == 45
