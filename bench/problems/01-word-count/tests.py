from solution import word_count


def test_empty():
    assert word_count("") == 0


def test_one():
    assert word_count("hello") == 1


def test_two():
    assert word_count("hello world") == 2


def test_leading():
    assert word_count("  hi") == 1


def test_trailing():
    assert word_count("hi  ") == 1


def test_collapse():
    assert word_count("a  b   c") == 3


def test_tabs():
    assert word_count("a\tb\tc") == 3
