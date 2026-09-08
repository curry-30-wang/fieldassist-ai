from __future__ import annotations

from collections.abc import Iterator
from datetime import datetime

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient
from sqlalchemy import create_engine, select
from sqlalchemy.orm import Session, sessionmaker

from backend.app.database import get_db
from backend.app.models import AiRun, Base, Conversation, Feedback, Message, Ticket, User
from backend.app.security import hash_password


DEMO_PASSWORD = "feedback-test-password"


@pytest.fixture
def feedback_client(
    tmp_path, monkeypatch
) -> Iterator[tuple[TestClient, sessionmaker[Session]]]:
    database_path = tmp_path / "fieldassist-feedback-test.db"
    monkeypatch.setenv("DATABASE_URL", f"sqlite:///{database_path.as_posix()}")
    monkeypatch.setenv("SECRET_KEY", "task-8-test-secret")
    monkeypatch.setenv("AI_PROVIDER", "mock")
    monkeypatch.setenv("OBSERVABILITY_PROVIDER", "mock")

    from backend.app.config import get_settings
    from backend.app.main import create_app

    get_settings.cache_clear()
    engine = create_engine(
        f"sqlite:///{database_path.as_posix()}",
        connect_args={"check_same_thread": False},
    )
    testing_session = sessionmaker(bind=engine, autoflush=False, autocommit=False)
    Base.metadata.create_all(bind=engine)
    with testing_session.begin() as db:
        db.add_all(
            [
                User(
                    email="admin@fieldassist.local",
                    display_name="Demo Admin",
                    password_hash=hash_password(DEMO_PASSWORD),
                    role="admin",
                    is_active=True,
                ),
                User(
                    email="employee@fieldassist.local",
                    display_name="Demo Employee",
                    password_hash=hash_password(DEMO_PASSWORD),
                    role="employee",
                    is_active=True,
                ),
                User(
                    email="employee-two@fieldassist.local",
                    display_name="Second Employee",
                    password_hash=hash_password(DEMO_PASSWORD),
                    role="employee",
                    is_active=True,
                ),
            ]
        )

    def override_get_db() -> Iterator[Session]:
        with testing_session() as db:
            yield db

    application: FastAPI = create_app()
    application.dependency_overrides[get_db] = override_get_db
    with TestClient(application) as client:
        yield client, testing_session

    application.dependency_overrides.clear()
    engine.dispose()
    get_settings.cache_clear()


def login(client: TestClient, email: str = "employee@fieldassist.local") -> None:
    response = client.post(
        "/api/auth/login",
        json={"email": email, "password": DEMO_PASSWORD},
    )
    assert response.status_code == 200


def logout(client: TestClient) -> None:
    response = client.post("/api/auth/logout")
    assert response.status_code == 200


def create_assistant_message(client: TestClient, title: str = "反馈测试") -> int:
    conversation = client.post("/api/conversations", json={"title": title})
    assert conversation.status_code == 201
    conversation_id = conversation.json()["data"]["conversation"]["id"]
    response = client.post(
        f"/api/conversations/{conversation_id}/messages",
        json={"question": "请说明年假申请时间"},
    )
    assert response.status_code == 200
    return response.json()["data"]["assistant_message"]["id"]


def test_employee_can_create_feedback_for_owned_assistant_message(feedback_client) -> None:
    client, testing_session = feedback_client
    login(client)
    message_id = create_assistant_message(client)

    response = client.post(
        f"/api/messages/{message_id}/feedback",
        json={"rating": True, "comment": "回答很有帮助"},
    )

    assert response.status_code == 201
    assert response.json()["data"]["feedback"]["rating"] is True
    with testing_session() as db:
        feedback = db.scalar(select(Feedback))
        assert feedback is not None
        assert feedback.rating == 1
        assert feedback.comment == "回答很有帮助"


def test_duplicate_feedback_updates_same_row(feedback_client) -> None:
    client, testing_session = feedback_client
    login(client)
    message_id = create_assistant_message(client)
    endpoint = f"/api/messages/{message_id}/feedback"

    first = client.post(endpoint, json={"rating": True, "comment": "先点赞"})
    second = client.post(endpoint, json={"rating": False, "comment": "需要补充依据"})

    assert first.status_code == 201
    assert second.status_code == 200
    assert second.json()["data"]["feedback"]["id"] == first.json()["data"]["feedback"]["id"]
    with testing_session() as db:
        feedbacks = db.scalars(select(Feedback)).all()
        assert len(feedbacks) == 1
        assert feedbacks[0].rating == 0
        assert feedbacks[0].comment == "需要补充依据"


def test_feedback_requires_assistant_message_and_owned_conversation(feedback_client) -> None:
    client, _testing_session = feedback_client
    login(client)
    conversation = client.post("/api/conversations", json={"title": "用户消息"})
    conversation_id = conversation.json()["data"]["conversation"]["id"]
    user_message = client.post(
        f"/api/conversations/{conversation_id}/messages",
        json={"question": "请说明年假申请时间"},
    )
    user_message_id = user_message.json()["data"]["user_message"]["id"]
    response = client.post(
        f"/api/messages/{user_message_id}/feedback",
        json={"rating": True},
    )
    assert response.status_code == 404
    assert response.json()["message"] == "助手消息不存在"

    logout(client)
    login(client, "employee-two@fieldassist.local")
    assistant_id = user_message.json()["data"]["assistant_message"]["id"]
    response = client.post(
        f"/api/messages/{assistant_id}/feedback",
        json={"rating": False},
    )
    assert response.status_code == 404
    assert response.json()["message"] == "助手消息不存在"


def test_feedback_commits_before_tracer_score_and_tracer_failure_is_safe(
    feedback_client, monkeypatch
) -> None:
    client, testing_session = feedback_client
    login(client)
    message_id = create_assistant_message(client)
    observed: dict[str, object] = {}

    class FailingTracer:
        def record_score(self, trace_id: str, name: str, value: float, comment: str | None) -> None:
            with testing_session() as db:
                saved = db.scalar(select(Feedback))
                observed["saved"] = saved is not None
                observed["trace_id"] = trace_id
                observed["value"] = value
                observed["comment"] = comment
            raise RuntimeError("tracer unavailable")

    monkeypatch.setattr(
        "backend.app.services.tickets.create_tracer",
        lambda _settings: FailingTracer(),
    )
    response = client.post(
        f"/api/messages/{message_id}/feedback",
        json={"rating": False, "comment": "回答没有解决问题"},
    )

    assert response.status_code == 201
    assert observed == {
        "saved": True,
        "trace_id": observed["trace_id"],
        "value": 0.0,
        "comment": "回答没有解决问题",
    }
    assert isinstance(observed["trace_id"], str)
    with testing_session() as db:
        run = db.scalar(select(AiRun).order_by(AiRun.id.desc()))
        assert run is not None
        assert observed["trace_id"] == run.trace_id


def test_employee_can_create_and_list_owned_ticket(feedback_client) -> None:
    client, _testing_session = feedback_client
    login(client)
    message_id = create_assistant_message(client)

    created = client.post(
        f"/api/messages/{message_id}/ticket",
        json={
            "title": "请补充年假政策说明",
            "description": "回答没有覆盖特殊情形，请人工确认。",
            "priority": "high",
        },
    )
    listed = client.get("/api/tickets")

    assert created.status_code == 201
    ticket = created.json()["data"]["ticket"]
    assert ticket["status"] == "open"
    assert ticket["priority"] == "high"
    assert listed.status_code == 200
    assert [item["id"] for item in listed.json()["data"]["tickets"]] == [ticket["id"]]


def test_employee_can_update_owned_ticket_and_status_transition_updates_timestamp(
    feedback_client,
) -> None:
    client, testing_session = feedback_client
    login(client)
    message_id = create_assistant_message(client)
    ticket_id = client.post(
        f"/api/messages/{message_id}/ticket",
        json={"title": "状态流转", "description": "需要处理", "priority": "medium"},
    ).json()["data"]["ticket"]["id"]
    with testing_session() as db:
        original = db.get(Ticket, ticket_id)
        assert original is not None
        original_updated_at = original.updated_at

    updated = client.patch(f"/api/tickets/{ticket_id}", json={"status": "in_progress"})
    repeated = client.patch(f"/api/tickets/{ticket_id}", json={"status": "in_progress"})

    assert updated.status_code == 200
    assert repeated.status_code == 200
    with testing_session() as db:
        ticket = db.get(Ticket, ticket_id)
        assert ticket is not None
        assert ticket.status == "in_progress"
        assert isinstance(ticket.updated_at, datetime)
        assert ticket.updated_at >= original_updated_at


def test_employee_cannot_read_or_update_another_employees_ticket(feedback_client) -> None:
    client, _testing_session = feedback_client
    login(client)
    message_id = create_assistant_message(client)
    ticket_id = client.post(
        f"/api/messages/{message_id}/ticket",
        json={"title": "私人工单", "description": "仅创建人可见", "priority": "low"},
    ).json()["data"]["ticket"]["id"]
    logout(client)
    login(client, "employee-two@fieldassist.local")

    listed = client.get("/api/tickets")
    updated = client.patch(f"/api/tickets/{ticket_id}", json={"status": "closed"})

    assert listed.status_code == 200
    assert listed.json()["data"]["tickets"] == []
    assert updated.status_code == 404
    assert updated.json()["message"] == "工单不存在"


def test_admin_can_create_list_and_update_any_ticket(feedback_client) -> None:
    client, _testing_session = feedback_client
    login(client)
    message_id = create_assistant_message(client)
    ticket_id = client.post(
        f"/api/messages/{message_id}/ticket",
        json={"title": "管理员处理", "description": "管理员可见", "priority": "urgent"},
    ).json()["data"]["ticket"]["id"]
    logout(client)
    login(client, "admin@fieldassist.local")

    listed = client.get("/api/tickets")
    updated = client.patch(f"/api/tickets/{ticket_id}", json={"status": "in_progress"})

    assert listed.status_code == 200
    assert [item["id"] for item in listed.json()["data"]["tickets"]] == [ticket_id]
    assert updated.status_code == 200
    assert updated.json()["data"]["ticket"]["status"] == "in_progress"


def test_admin_can_create_ticket_from_any_assistant_message(feedback_client) -> None:
    client, _testing_session = feedback_client
    login(client)
    message_id = create_assistant_message(client)
    logout(client)
    login(client, "admin@fieldassist.local")

    response = client.post(
        f"/api/messages/{message_id}/ticket",
        json={"title": "代员工建单", "description": "管理员补充", "priority": "low"},
    )

    assert response.status_code == 201


def test_invalid_ticket_status_transition_is_rejected(feedback_client) -> None:
    client, _testing_session = feedback_client
    login(client)
    message_id = create_assistant_message(client)
    ticket_id = client.post(
        f"/api/messages/{message_id}/ticket",
        json={"title": "非法流转", "description": "需要拒绝", "priority": "low"},
    ).json()["data"]["ticket"]["id"]

    response = client.patch(f"/api/tickets/{ticket_id}", json={"status": "resolved"})

    assert response.status_code == 422
    assert response.json()["message"] == "不允许的工单状态流转"


@pytest.mark.parametrize(
    ("endpoint_suffix", "payload"),
    (
        ("/feedback", {"rating": True, "comment": "x" * 501}),
        ("/ticket", {"title": "x" * 256, "description": "有效描述", "priority": "low"}),
        ("/ticket", {"title": "有效标题", "description": "x" * 2001, "priority": "low"}),
        ("/ticket", {"title": "   ", "description": "有效描述", "priority": "low"}),
        ("/ticket", {"title": "有效标题", "description": "   ", "priority": "low"}),
        ("/ticket", {"title": "有效标题", "description": "有效描述", "priority": "invalid"}),
    ),
)
def test_feedback_and_ticket_text_validation_is_safe(
    feedback_client, endpoint_suffix: str, payload: dict[str, object]
) -> None:
    client, _testing_session = feedback_client
    login(client)
    message_id = create_assistant_message(client)

    response = client.post(f"/api/messages/{message_id}{endpoint_suffix}", json=payload)

    assert response.status_code == 422
    assert response.json() == {
        "success": False,
        "data": None,
        "message": "请求参数格式错误",
    }


def test_automation_boundary_is_noop_without_network() -> None:
    from backend.app.integrations.contracts import NoopAutomationClient

    client = NoopAutomationClient()
    assert client.create_external_ticket(object()) is None
    assert client.notify_ticket(object()) is None
