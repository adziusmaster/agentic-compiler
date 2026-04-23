from solution import reverse_words


def test_empty():
    assert reverse_words("") == ""


def test_single():
    assert reverse_words("a") == "a"


def test_two():
    assert reverse_words("hello world") == "world hello"


def test_three():
    assert reverse_words("one two three") == "three two one"


def test_four():
    assert reverse_words("the quick brown fox") == "fox brown quick the"
