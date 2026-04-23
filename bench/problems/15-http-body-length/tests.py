from solution import body_length


def fixed(text):
    return lambda u: text


def test_hello_body():
    assert body_length("https://x/data", fixed("hello")) == 5


def test_empty_body():
    assert body_length("https://x/data", fixed("")) == 0


def test_json_body():
    assert body_length("https://x/data", fixed('{"a":1}')) == 7


def test_passes_url():
    seen = []
    body_length("https://a/b", lambda u: seen.append(u) or "")
    assert seen == ["https://a/b"]
