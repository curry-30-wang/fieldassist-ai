from __future__ import annotations

from pathlib import Path

from fastapi.testclient import TestClient


FRONTEND_DIRECTORY = Path(__file__).resolve().parents[1] / "frontend"


def _frontend_source() -> str:
    return "\n".join(
        path.read_text(encoding="utf-8")
        for path in (
            FRONTEND_DIRECTORY / "index.html",
            FRONTEND_DIRECTORY / "app.js",
            FRONTEND_DIRECTORY / "styles.css",
        )
    )


def test_root_serves_vue_application_shell_and_same_origin_api(client: TestClient) -> None:
    response = client.get("/")

    assert response.status_code == 200
    assert '<div id="app">' in response.text
    assert 'https://unpkg.com/vue@3/dist/vue.global.prod.js' in response.text
    assert '<script src="/app.js"></script>' in response.text
    assert "/api/health" in _frontend_source()


def test_frontend_uses_safe_session_requests_and_role_workbenches() -> None:
    source = _frontend_source()

    assert 'credentials: "same-origin"' in source
    for required_text in (
        "/api/auth/login",
        "/api/auth/me",
        "/api/conversations",
        "/api/messages/",
        "/api/tickets",
        "/api/admin/summary",
        "/api/admin/health/integrations",
        "/api/admin/documents",
        "/api/admin/evaluations/run",
        "员工工作台",
        "管理员工作台",
        "会话已失效",
        "加载中",
        "暂无数据",
    ):
        assert required_text in source


def test_frontend_contains_no_provider_secret_identifiers_or_values() -> None:
    source = _frontend_source()

    for secret_name in (
        "DIFY_API_KEY",
        "LANGFUSE_SECRET_KEY",
        "LANGFUSE_PUBLIC_KEY",
        "SECRET_KEY",
        "Authorization: Bearer",
    ):
        assert secret_name not in source
