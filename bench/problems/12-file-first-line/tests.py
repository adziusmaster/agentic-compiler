from solution import first_line


def fixed(text):
    return lambda p: text


def test_two_line():
    assert first_line("/etc/hosts", fixed("alpha\nbeta")) == "alpha"


def test_trailing_newline():
    assert first_line("/etc/hosts", fixed("only\n")) == "only"


def test_no_newline():
    assert first_line("/etc/hosts", fixed("whole")) == "whole"


def test_passes_path():
    seen = []
    first_line("/tmp/x", lambda p: seen.append(p) or "")
    assert seen == ["/tmp/x"]
