from solution import home_or


def test_empty_falls_back():
    assert home_or("/nowhere", lambda k: "") == "/nowhere"


def test_present_wins():
    assert home_or("/nowhere", lambda k: "/root") == "/root"


def test_queries_home_key():
    seen = []
    home_or("/x", lambda k: seen.append(k) or "")
    assert seen == ["HOME"]
