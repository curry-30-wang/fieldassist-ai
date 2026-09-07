from fastapi import FastAPI
from fastapi.testclient import TestClient


def test_create_app_returns_fieldassist_fastapi_application(app: FastAPI) -> None:
    assert isinstance(app, FastAPI)
    assert app.title == "FieldAssist"


def test_health_reports_active_offline_providers(client: TestClient) -> None:
    response = client.get("/api/health")

    assert response.status_code == 200
    assert response.json() == {
        "success": True,
        "data": {
            "status": "ok",
            "ai_provider": "mock",
            "observability_provider": "mock",
        },
        "message": "FieldAssist is healthy",
    }


def test_root_serves_vue_page(client: TestClient) -> None:
    response = client.get("/")

    assert response.status_code == 200
    assert "text/html" in response.headers["content-type"]
    assert '<div id="app">' in response.text
    assert '<script src="/app.js"></script>' in response.text
