from solution import is_enabled


def env_of(val):
    return lambda k: val


def test_empty():      assert is_enabled(env_of("")) == 0
def test_zero_str():   assert is_enabled(env_of("0")) == 0
def test_one_str():    assert is_enabled(env_of("1")) == 1
def test_true_lc():    assert is_enabled(env_of("true")) == 1
def test_true_uc():    assert is_enabled(env_of("TRUE")) == 1
def test_yes_mixed():  assert is_enabled(env_of("Yes")) == 1
def test_on_mixed():   assert is_enabled(env_of("On")) == 1
def test_garbage():    assert is_enabled(env_of("nope")) == 0
