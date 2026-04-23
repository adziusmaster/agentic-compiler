from solution import db_url


def env_of(mapping):
    return lambda k: mapping.get(k, "")


def test_both_present():
    assert db_url(env_of({"DB_HOST": "db.example.com", "DB_PORT": "5555"})) \
        == "db.example.com:5555"


def test_host_missing():
    assert db_url(env_of({"DB_PORT": "5555"})) == "localhost:5432"


def test_port_missing():
    assert db_url(env_of({"DB_HOST": "db.example.com"})) == "localhost:5432"


def test_both_missing():
    assert db_url(env_of({})) == "localhost:5432"
