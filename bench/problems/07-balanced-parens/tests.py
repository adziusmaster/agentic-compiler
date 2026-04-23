from solution import is_balanced


def test_empty():     assert is_balanced("") == 1
def test_pair():      assert is_balanced("()") == 1
def test_nested():    assert is_balanced("(())") == 1
def test_siblings():  assert is_balanced("(()())") == 1
def test_ignore():    assert is_balanced("(a(b)c)") == 1
def test_reversed():  assert is_balanced(")(") == 0
def test_unclosed():  assert is_balanced("((") == 0
def test_trailing():  assert is_balanced("())") == 0
def test_bare_text(): assert is_balanced("abc") == 1
