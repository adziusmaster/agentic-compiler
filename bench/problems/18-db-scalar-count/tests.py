from solution import count_users


def answering(result):
    def fn(conn, sql):
        assert sql == "SELECT COUNT(*) FROM users"
        return result
    return fn


def test_zero():
    assert count_users(":memory:", answering("0")) == 0


def test_three():
    assert count_users(":memory:", answering("3")) == 3


def test_large():
    assert count_users(":memory:", answering("1000")) == 1000
