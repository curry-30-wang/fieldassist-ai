from __future__ import annotations

import json
from collections.abc import Iterator

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient
from sqlalchemy import create_engine, select
from sqlalchemy.orm import Session, sessionmaker

from backend.app.database import get_db
from backend.app.integrations.contracts import ChatResult, IntegrationTimeoutError
from backend.app.models import (
    AiRun,
    Base,
    Conversation,
    EvaluationCase,
    EvaluationResult,
    EvaluationRun,
    Feedback,
    KnowledgeDocument,
    Message,
    Ticket,
    User,
)
from backend.app.security import hash_password


DEMO_PASSWORD = "admin-evaluation-password"


@pytest.fixture
def admin_client(tmp_path, monkeypatch) -> Iterator[tuple[TestClient, sessionmaker[Session]]]:
    database_path = tmp_path / "fieldassist-admin-test.db"
    monkeypatch.setenv("DATABASE_URL", f"sqlite:///{database_path.as_posix()}")
    monkeypatch.setenv("SECRET_KEY", "task-9-test-secret")
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
                KnowledgeDocument(
                    title="员工请假政策",
                    category="leave_policy",
                    content="员工申请年假应至少提前三个工作日提交，并经直属主管审批。内部机密内容不应返回。",
                    source_url="https://example.invalid/leave-policy",
                    enabled=True,
                ),
                KnowledgeDocument(
                    title="已停用的内部文档",
                    category="disabled",
                    content="不应出现在管理员文档列表中。",
                    source_url="https://example.invalid/disabled",
                    enabled=False,
                ),
                EvaluationCase(
                    question="年假需要提前多久申请？",
                    category="leave_policy",
                    expected_keywords_json=json.dumps(["年假", "审批"], ensure_ascii=False),
                ),
                EvaluationCase(
                    question="费用报销需要什么？",
                    category="expense_reimbursement",
                    expected_keywords_json=json.dumps(["费用", "不存在关键词"], ensure_ascii=False),
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


def login(client: TestClient, email: str) -> None:
    response = client.post(
        "/api/auth/login",
        json={"email": email, "password": DEMO_PASSWORD},
    )
    assert response.status_code == 200


def test_every_admin_endpoint_rejects_employee_and_unauthenticated_callers(admin_client) -> None:
    client, _testing_session = admin_client
    endpoints = (
        ("GET", "/api/admin/summary", None),
        ("GET", "/api/admin/health/integrations", None),
        ("GET", "/api/admin/documents", None),
        ("POST", "/api/admin/evaluations/run", None),
        ("GET", "/api/admin/evaluations/1", None),
    )

    for method, endpoint, payload in endpoints:
        response = client.request(method, endpoint, json=payload)
        assert response.status_code == 401
        assert response.json()["data"] is None

    login(client, "employee@fieldassist.local")
    for method, endpoint, payload in endpoints:
        response = client.request(method, endpoint, json=payload)
        assert response.status_code == 403
        assert response.json()["message"] == "需要管理员权限"


def test_summary_calculates_questions_latency_feedback_tickets_and_providers(admin_client) -> None:
    client, testing_session = admin_client
    with testing_session.begin() as db:
        employee = db.scalar(select(User).where(User.role == "employee"))
        assert employee is not None
        conversation = Conversation(user_id=employee.id, title="指标会话")
        db.add(conversation)
        db.flush()
        messages = [
            Message(conversation_id=conversation.id, role="user", content="问题一"),
            Message(conversation_id=conversation.id, role="user", content="问题二"),
            Message(conversation_id=conversation.id, role="user", content="问题三"),
            Message(conversation_id=conversation.id, role="assistant", content="回答一"),
            Message(conversation_id=conversation.id, role="assistant", content="回答二"),
        ]
        db.add_all(messages)
        db.flush()
        db.add_all(
            [
                AiRun(
                    message_id=messages[3].id,
                    provider="mock",
                    status="success",
                    latency_ms=100,
                ),
                AiRun(
                    message_id=messages[4].id,
                    provider="mock",
                    status="success",
                    latency_ms=300,
                ),
                AiRun(
                    message_id=messages[3].id,
                    provider="dify",
                    status="failed",
                    latency_ms=999,
                ),
            ]
        )
        db.add_all(
            [
                Feedback(message_id=messages[3].id, user_id=employee.id, rating=1),
                Feedback(message_id=messages[4].id, user_id=employee.id, rating=0),
            ]
        )
        db.add_all(
            [
                Ticket(
                    source_message_id=messages[3].id,
                    created_by=employee.id,
                    title="开放工单",
                    description="需要处理",
                    priority="high",
                    status="open",
                ),
                Ticket(
                    source_message_id=messages[4].id,
                    created_by=employee.id,
                    title="处理中工单",
                    description="处理中",
                    priority="medium",
                    status="in_progress",
                ),
                Ticket(
                    source_message_id=messages[3].id,
                    created_by=employee.id,
                    title="已关闭工单",
                    description="已完成",
                    priority="low",
                    status="closed",
                ),
            ]
        )

    login(client, "admin@fieldassist.local")
    response = client.get("/api/admin/summary")

    assert response.status_code == 200
    summary = response.json()["data"]
    assert summary["total_questions"] == 3
    assert summary["average_latency_ms"] == 200
    assert summary["positive_feedback_rate"] == pytest.approx(0.5)
    assert summary["open_ticket_count"] == 2
    assert summary["provider_breakdown"] == {"mock": 2, "dify": 1}


def test_summary_is_zero_safe_without_usage_records(admin_client) -> None:
    client, _testing_session = admin_client
    login(client, "admin@fieldassist.local")

    response = client.get("/api/admin/summary")

    assert response.status_code == 200
    assert response.json()["data"] == {
        "total_questions": 0,
        "average_latency_ms": 0,
        "positive_feedback_rate": 0,
        "open_ticket_count": 0,
        "provider_breakdown": {},
    }


def test_mock_health_returns_only_safe_status_fields_without_external_calls(admin_client) -> None:
    client, _testing_session = admin_client
    login(client, "admin@fieldassist.local")

    response = client.get("/api/admin/health/integrations")

    assert response.status_code == 200
    health = response.json()["data"]
    assert set(health) == {"application", "ai_provider", "observability_provider", "database"}
    for component in health.values():
        assert set(component) <= {"configured", "reachable", "error_code"}
        assert component["configured"] is True
        assert component["reachable"] is True
    assert "base_url" not in response.text
    assert "api_key" not in response.text
    assert "secret_key" not in response.text


def test_documents_return_enabled_metadata_without_content(admin_client) -> None:
    client, _testing_session = admin_client
    login(client, "admin@fieldassist.local")

    response = client.get("/api/admin/documents")

    assert response.status_code == 200
    documents = response.json()["data"]["documents"]
    assert len(documents) == 1
    assert set(documents[0]) == {"id", "title", "category", "source_url", "enabled", "created_at"}
    assert documents[0]["title"] == "员工请假政策"
    assert documents[0]["enabled"] is True
    assert "内部机密内容" not in response.text
    assert "content" not in documents[0]


def test_mock_evaluation_persists_ordered_results_and_requires_all_keywords(admin_client) -> None:
    client, testing_session = admin_client
    login(client, "admin@fieldassist.local")

    started = client.post("/api/admin/evaluations/run")

    assert started.status_code == 200
    run_id = started.json()["data"]["run_id"]
    detail = client.get(f"/api/admin/evaluations/{run_id}")
    assert detail.status_code == 200
    data = detail.json()["data"]
    assert data["run"]["id"] == run_id
    assert data["run"]["provider"] == "mock"
    assert data["run"]["total_cases"] == 2
    assert data["run"]["passed_cases"] == 1
    assert data["run"]["score"] == pytest.approx(0.5)
    assert [result["case_id"] for result in data["results"]] == sorted(
        result["case_id"] for result in data["results"]
    )
    assert [result["matched"] for result in data["results"]] == [True, False]

    with testing_session() as db:
        run = db.get(EvaluationRun, run_id)
        assert run is not None
        saved_results = db.scalars(
            select(EvaluationResult)
            .where(EvaluationResult.run_id == run_id)
            .order_by(EvaluationResult.case_id, EvaluationResult.id)
        ).all()
        assert len(saved_results) == 2
        assert all(result.answer for result in saved_results)


def test_evaluation_continues_after_one_provider_failure_and_saves_safe_error(admin_client, monkeypatch) -> None:
    client, testing_session = admin_client
    login(client, "admin@fieldassist.local")
    calls: list[str] = []

    class PartiallyFailingProvider:
        provider = "mock"

        def chat(self, question: str, user_id: str, conversation_id: str | None) -> ChatResult:
            del user_id, conversation_id
            calls.append(question)
            if question == "费用报销需要什么？":
                raise IntegrationTimeoutError
            return ChatResult(
                answer="年假需要提前三个工作日，并经主管审批。",
                conversation_id=None,
                sources=[],
                provider="mock",
                model="test-model",
                raw_metadata={"latency_ms": 7},
            )

        def health_check(self):
            raise AssertionError("evaluation should not call provider health")

    provider = PartiallyFailingProvider()
    monkeypatch.setattr(
        "backend.app.services.evaluations.create_chat_provider",
        lambda db, settings: provider,
    )

    started = client.post("/api/admin/evaluations/run")

    assert started.status_code == 200
    run_id = started.json()["data"]["run_id"]
    assert calls == ["年假需要提前多久申请？", "费用报销需要什么？"]
    results = client.get(f"/api/admin/evaluations/{run_id}").json()["data"]["results"]
    assert [result["matched"] for result in results] == [True, False]
    assert results[1]["answer"] == ""
    assert results[1]["error_message"] == "AI 服务响应超时，请稍后重试"

    with testing_session() as db:
        run = db.get(EvaluationRun, run_id)
        assert run is not None
        assert run.passed_cases == 1
        assert run.score == pytest.approx(0.5)
