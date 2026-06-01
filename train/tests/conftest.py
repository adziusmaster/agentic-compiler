"""pytest config: ensure `train/` modules are importable from tests."""
import sys
from pathlib import Path

TRAIN_DIR = Path(__file__).resolve().parent.parent
if str(TRAIN_DIR) not in sys.path:
    sys.path.insert(0, str(TRAIN_DIR))
