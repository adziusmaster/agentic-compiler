from solution import copy_file


def fs(source_text):
    writes = []
    def read(p): return source_text
    def write(p, body):
        writes.append((p, body))
        return 1
    return read, write, writes


def test_copies_five():
    r, w, writes = fs("hello")
    assert copy_file("/src.txt", "/dst.txt", r, w) == 5
    assert writes == [("/dst.txt", "hello")]


def test_copies_empty():
    r, w, writes = fs("")
    assert copy_file("/src.txt", "/dst.txt", r, w) == 0
    assert writes == [("/dst.txt", "")]


def test_copies_long():
    r, w, writes = fs("abcdefghijklmnopqrst")
    assert copy_file("/src.txt", "/dst.txt", r, w) == 20
