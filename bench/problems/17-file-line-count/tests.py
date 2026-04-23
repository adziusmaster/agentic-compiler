from solution import line_count


def fixed(text):
    return lambda p: text


def test_empty():          assert line_count("/f", fixed("")) == 0
def test_one_terminated(): assert line_count("/f", fixed("a\n")) == 1
def test_two_terminated(): assert line_count("/f", fixed("a\nb\n")) == 2
def test_two_no_final():   assert line_count("/f", fixed("a\nb")) == 2
def test_just_newline():   assert line_count("/f", fixed("\n")) == 1
def test_double_nl():      assert line_count("/f", fixed("\n\n")) == 2
