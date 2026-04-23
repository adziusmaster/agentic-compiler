from solution import reverse_string


def test_empty():      assert reverse_string("") == ""
def test_single():     assert reverse_string("a") == "a"
def test_abc():        assert reverse_string("abc") == "cba"
def test_hello():      assert reverse_string("hello world") == "dlrow olleh"
def test_palindrome(): assert reverse_string("level") == "level"
def test_digits():     assert reverse_string("12345") == "54321"
