from solution import port_or


def test_default_when_empty():
    assert port_or(8080, lambda k: "") == 8080


def test_parses_present():
    assert port_or(8080, lambda k: "3000") == 3000


def test_reads_zero():
    assert port_or(8080, lambda k: "0") == 0


def test_queries_port_key():
    seen = []
    port_or(80, lambda k: seen.append(k) or "")
    assert seen == ["PORT"]
