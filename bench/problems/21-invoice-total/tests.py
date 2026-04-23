from solution import invoice_total


def approx(a, b, tol=0.01):
    return abs(a - b) <= tol


def test_plain():         assert approx(invoice_total(10, 2, 0, 0, 0), 20)
def test_with_tax():      assert approx(invoice_total(10, 2, 0, 10, 0), 22)
def test_with_discount(): assert approx(invoice_total(10, 2, 10, 0, 0), 18)
def test_shipping_only(): assert approx(invoice_total(0, 0, 0, 0, 5), 5)
def test_full():          assert approx(invoice_total(100, 3, 10, 20, 5), 329)
def test_zero_qty():      assert approx(invoice_total(50, 0, 10, 20, 5), 5)
