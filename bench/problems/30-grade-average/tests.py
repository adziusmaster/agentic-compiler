from solution import letter_grade


def test_perfect():   assert letter_grade(100, 100, 100, 100) == "A"
def test_mixed_b():   assert letter_grade(95, 88, 72, 60) == "B"
def test_low_f():     assert letter_grade(60, 55, 50, 45) == "F"
def test_high_a():    assert letter_grade(100, 90, 90, 50) == "A"
def test_drop_zero(): assert letter_grade(80, 80, 80, 0) == "B"
def test_c_grade():   assert letter_grade(70, 70, 70, 10) == "C"
def test_d_grade():   assert letter_grade(65, 65, 65, 0) == "D"
