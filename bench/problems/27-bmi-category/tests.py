from solution import bmi_category


def test_thin():     assert bmi_category(45, 170) == "underweight"
def test_normal():   assert bmi_category(70, 175) == "normal"
def test_over():     assert bmi_category(85, 175) == "overweight"
def test_obese():    assert bmi_category(110, 175) == "obese"
def test_boundary(): assert bmi_category(76.5625, 175) == "overweight"
