import os
from pathlib import Path
import sys

import uvicorn


PROJECT_ROOT = Path(__file__).resolve().parent.parent
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from backend.app.main import app


def get_host() -> str:
    return os.getenv("HOST", "127.0.0.1")


if __name__ == "__main__":
    uvicorn.run(app, host=get_host(), port=8000)
