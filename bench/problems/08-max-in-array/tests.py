from solution import arr_max


def test_single():      assert arr_max([42], 1) == 42
def test_eight():       assert arr_max([3, 1, 4, 1, 5, 9, 2, 6], 8) == 9
def test_negatives():   assert arr_max([-7, -2, -5], 3) == -2
def test_prefix_only(): assert arr_max([1, 2, 3, 100, 100], 3) == 3
