from pathlib import Path

import yaml
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


def test_vue_scripts_load_after_mount_target(client: TestClient) -> None:
    html = client.get("/").text

    mount_target = html.index('<div id="app">')
    vue_script = html.index('<script src="https://unpkg.com/vue@3/dist/vue.global.prod.js"></script>')
    app_script = html.index('<script src="/app.js"></script>')
    body_end = html.index("</body>")

    assert mount_target < vue_script < app_script < body_end


def test_compose_host_override_preserves_local_loopback_default(monkeypatch) -> None:
    from backend import run

    monkeypatch.delenv("HOST", raising=False)
    assert run.get_host() == "127.0.0.1"

    compose_path = Path(__file__).resolve().parents[1] / "compose.yaml"
    compose = yaml.safe_load(compose_path.read_text(encoding="utf-8"))
    compose_host = compose["services"]["fieldassist"]["environment"]["HOST"]

    assert compose_host == "0.0.0.0"
    monkeypatch.setenv("HOST", compose_host)
    assert run.get_host() == "0.0.0.0"
