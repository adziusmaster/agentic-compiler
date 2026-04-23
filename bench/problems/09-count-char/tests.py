from solution import count_char


def test_empty():     assert count_char("", "a") == 0
def test_none():      assert count_char("banana", "z") == 0
def test_three():     assert count_char("banana", "a") == 3
def test_case():      assert count_char("Banana", "a") == 3
def test_case_none(): assert count_char("Banana", "A") == 0
def test_all():       assert count_char("aaaa", "a") == 4
def test_spaces():    assert count_char("a b c d", " ") == 3
