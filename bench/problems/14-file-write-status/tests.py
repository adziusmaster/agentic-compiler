from solution import save_status


def recording():
    log = []
    def write(path, body):
        log.append((path, body))
        return 1
    return log, write


def test_returns_write_code():
    log, w = recording()
    assert save_status("/tmp/s.txt", 7, w) == 1


def test_writes_correct_body():
    log, w = recording()
    save_status("/tmp/s.txt", 7, w)
    assert log == [("/tmp/s.txt", "OK count=7")]


def test_zero_count():
    log, w = recording()
    save_status("/tmp/s.txt", 0, w)
    assert log == [("/tmp/s.txt", "OK count=0")]
