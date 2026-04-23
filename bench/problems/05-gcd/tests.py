from solution import gcd


def test_zero_a():  assert gcd(0, 5) == 5
def test_zero_b():  assert gcd(7, 0) == 7
def test_same():    assert gcd(6, 6) == 6
def test_two():     assert gcd(12, 18) == 6
def test_coprime(): assert gcd(17, 5) == 1
def test_large():   assert gcd(100, 75) == 25
def test_pow2():    assert gcd(48, 36) == 12
