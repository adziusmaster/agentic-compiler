from solution import fib


def test_f0():  assert fib(0) == 0
def test_f1():  assert fib(1) == 1
def test_f2():  assert fib(2) == 1
def test_f3():  assert fib(3) == 2
def test_f5():  assert fib(5) == 5
def test_f10(): assert fib(10) == 55
def test_f20(): assert fib(20) == 6765
def test_f30(): assert fib(30) == 832040
