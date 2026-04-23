from solution import is_prime


def test_zero():    assert is_prime(0) == 0
def test_one():     assert is_prime(1) == 0
def test_two():     assert is_prime(2) == 1
def test_three():   assert is_prime(3) == 1
def test_four():    assert is_prime(4) == 0
def test_seven():   assert is_prime(7) == 1
def test_nine():    assert is_prime(9) == 0
def test_eleven():  assert is_prime(11) == 1
def test_fifteen(): assert is_prime(15) == 0
def test_p29():     assert is_prime(29) == 1
