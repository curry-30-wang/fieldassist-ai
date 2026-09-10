import json
from collections.abc import Iterator

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient
from sqlalchemy import create_engine, select
from sqlalchemy.orm import Session, sessionmaker

from backend.app.database import get_db
from backend.app.models import AiRun, Base, Conversation, KnowledgeDocument, Message, User
from backend.app.security import hash_password


DEMO_PASSWORD = "chat-test-password"


@pytest.fixture
def chat_client(tmp_path, monkeypatch) -> Iterator[tuple[TestClient, sessionmaker[Session]]]:
    database_path = tmp_path / "fieldassist-chat-test.db"
    monkeypatch.setenv("DATABASE_URL", f"sqlite:///{database_path.as_posix()}")
    monkeypatch.setenv("SECRET_KEY", "task-7-test-secret")
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
                KnowledgeDocument(
                    title="员工请假政策",
                    category="leave_policy",
                    content="员工申请年假应至少提前三个工作日提交，并经直属主管审批；紧急病假可先通知主管后补交材料。",
                    source_url="https://example.invalid/leave-policy",
                    enabled=True,
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
    assert response.json()["data"]["user"]["role"] == "employee"


def test_employee_can_create_list_and_read_owned_conversation(chat_client) -> None:
    client, _testing_session = chat_client
    login(client)

    created = client.post("/api/conversations", json={"title": "请假政策咨询"})
    assert created.status_code == 201
    conversation = created.json()["data"]["conversation"]
    conversation_id = conversation["id"]
    assert conversation["title"] == "请假政策咨询"

    listed = client.get("/api/conversations")
    assert listed.status_code == 200
    assert [item["id"] for item in listed.json()["data"]["conversations"]] == [conversation_id]

    detail = client.get(f"/api/conversations/{conversation_id}")
    assert detail.status_code == 200
    assert detail.json()["data"]["conversation"]["messages"] == []


def test_known_and_unknown_questions_persist_messages_sources_and_ai_runs(chat_client) -> None:
    client, testing_session = chat_client
    login(client)
    conversation_id = client.post(
        "/api/conversations", json={"title": "知识库问答"}
    ).json()["data"]["conversation"]["id"]

    known = client.post(
        f"/api/conversations/{conversation_id}/messages",
        json={"question": "年假需要提前多久申请？"},
    )
    assert known.status_code == 200
    known_data = known.json()["data"]
    assert known_data["user_message"]["role"] == "user"
    assert known_data["user_message"]["content"] == "年假需要提前多久申请？"
    assert "三个工作日" in known_data["assistant_message"]["content"]
    assert known_data["sources"][0]["title"] == "员工请假政策"
    assert known_data["provider"] == "mock"
    assert known_data["ai_run"]["status"] == "success"
    assert known_data["ai_run"]["provider"] == "mock"
    assert known_data["ai_run"]["trace_id"].startswith("mock-")

    unknown = client.post(
        f"/api/conversations/{conversation_id}/messages",
        json={"question": "公司食堂今天供应什么甜点？"},
    )
    assert unknown.status_code == 200
    unknown_data = unknown.json()["data"]
    assert "没有足够依据" in unknown_data["assistant_message"]["content"]
    assert unknown_data["sources"] == []
    assert unknown_data["ai_run"]["status"] == "success"

    detail = client.get(f"/api/conversations/{conversation_id}")
    messages = detail.json()["data"]["conversation"]["messages"]
    assert [message["role"] for message in messages] == [
        "user",
        "assistant",
        "user",
        "assistant",
    ]
    with testing_session() as db:
        saved_messages = db.scalars(
            select(Message).where(Message.conversation_id == conversation_id).order_by(Message.id)
        ).all()
        saved_runs = db.scalars(select(AiRun).order_by(AiRun.id)).all()
        assert len(saved_messages) == 4
        assert len(saved_runs) == 2
        assert all(run.status == "success" for run in saved_runs)
        assert json.loads(saved_messages[1].sources_json)[0]["title"] == "员工请假政策"


def test_conversation_list_is_ordered_by_newest_activity(chat_client) -> None:
    client, _testing_session = chat_client
    login(client)
    first_id = client.post(
        "/api/conversations", json={"title": "旧会话"}
    ).json()["data"]["conversation"]["id"]
    second_id = client.post(
        "/api/conversations", json={"title": "新会话"}
    ).json()["data"]["conversation"]["id"]

    client.post(
        f"/api/conversations/{first_id}/messages",
        json={"question": "年假需要提前多久申请？"},
    )
    listed_ids = [
        item["id"] for item in client.get("/api/conversations").json()["data"]["conversations"]
    ]
    assert listed_ids[:2] == [first_id, second_id]


def test_employee_cannot_read_or_post_to_another_employees_conversation(chat_client) -> None:
    client, _testing_session = chat_client
    login(client)
    conversation_id = client.post(
        "/api/conversations", json={"title": "私有会话"}
    ).json()["data"]["conversation"]["id"]

    client.post("/api/auth/logout")
    login(client, "employee-two@fieldassist.local")

    read = client.get(f"/api/conversations/{conversation_id}")
    post = client.post(
        f"/api/conversations/{conversation_id}/messages",
        json={"question": "年假需要提前多久申请？"},
    )
    assert read.status_code == 404
    assert post.status_code == 404
    assert read.json() == {"success": False, "data": None, "message": "会话不存在"}
    assert post.json() == {"success": False, "data": None, "message": "会话不存在"}


@pytest.mark.parametrize(
    ("endpoint", "payload"),
    (
        ("/api/conversations", {"title": "   "}),
        ("/api/conversations", {"title": "x" * 256}),
    ),
)
def test_conversation_title_validation_is_safe(chat_client, endpoint: str, payload: dict) -> None:
    client, _testing_session = chat_client
    login(client)

    response = client.post(endpoint, json=payload)
    assert response.status_code == 422
    assert response.json() == {
        "success": False,
        "data": None,
        "message": "请求参数格式错误",
    }


def test_question_validation_is_safe_and_does_not_echo_text(chat_client) -> None:
    client, _testing_session = chat_client
    login(client)
    conversation_id = client.post(
        "/api/conversations", json={"title": "参数校验"}
    ).json()["data"]["conversation"]["id"]
    secret_question = "secret-question-" + "x" * 2000

    response = client.post(
        f"/api/conversations/{conversation_id}/messages",
        json={"question": secret_question},
    )
    assert response.status_code == 422
    assert response.json() == {
        "success": False,
        "data": None,
        "message": "请求参数格式错误",
    }
    assert "secret-question" not in response.text


def test_provider_failure_preserves_user_message_and_saves_failed_run(chat_client, monkeypatch) -> None:
    client, testing_session = chat_client
    login(client)
    conversation_id = client.post(
        "/api/conversations", json={"title": "失败重试"}
    ).json()["data"]["conversation"]["id"]

    from backend.app.integrations.contracts import IntegrationTimeoutError
    from backend.app.integrations.factory import create_chat_provider

    class FailingProvider:
        def chat(self, question: str, user_id: str, conversation_id: str | None):
            raise IntegrationTimeoutError

    monkeypatch.setattr(
        "backend.app.services.chat.create_chat_provider",
        lambda db, settings: FailingProvider(),
    )
    response = client.post(
        f"/api/conversations/{conversation_id}/messages",
        json={"question": "年假需要提前多久申请？"},
    )

    assert response.status_code == 502
    assert response.json() == {
        "success": False,
        "data": None,
        "message": "AI 服务响应超时，请稍后重试",
    }
    with testing_session() as db:
        messages = db.scalars(
            select(Message).where(Message.conversation_id == conversation_id).order_by(Message.id)
        ).all()
        runs = db.scalars(select(AiRun).order_by(AiRun.id)).all()
        assert [message.role for message in messages] == ["user"]
        assert messages[0].content == "年假需要提前多久申请？"
        assert len(runs) == 1
        assert runs[0].status == "failed"
        assert runs[0].error_code == "integration_timeout"
        assert runs[0].latency_ms is not None
