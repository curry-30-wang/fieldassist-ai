from collections.abc import Iterator

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient


@pytest.fixture
def app(tmp_path, monkeypatch) -> Iterator[FastAPI]:
    database_path = tmp_path / "fieldassist-test.db"
    monkeypatch.setenv("DATABASE_URL", f"sqlite:///{database_path.as_posix()}")
    monkeypatch.setenv("AI_PROVIDER", "mock")
    monkeypatch.setenv("OBSERVABILITY_PROVIDER", "mock")

    from backend.app.config import get_settings
    from backend.app.main import create_app

    get_settings.cache_clear()
    application = create_app()
    yield application
    get_settings.cache_clear()


@pytest.fixture
def client(app: FastAPI) -> Iterator[TestClient]:
    with TestClient(app) as test_client:
        yield test_client
