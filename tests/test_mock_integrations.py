import inspect
import json

import pytest
from sqlalchemy import create_engine, select
from sqlalchemy.orm import Session

from backend.app.config import Settings
from backend.app.integrations.contracts import ChatProvider, Tracer
from backend.app.integrations.factory import create_chat_provider, create_tracer
from backend.app.integrations.mock_ai import MockChatProvider, NO_EVIDENCE_ANSWER
from backend.app.integrations.mock_trace import MockTracer
from backend.app.models import AiRun, Base, Conversation, KnowledgeDocument, Message, User
from backend.app.services.ai_runs import record_ai_failure, record_ai_run


@pytest.fixture
def db() -> Session:
    engine = create_engine("sqlite:///:memory:")
    Base.metadata.create_all(engine)
    with Session(engine) as session:
        user = User(
            email="employee@example.com",
            display_name="测试员工",
            password_hash="unused",
            role="employee",
        )
        session.add(user)
        session.flush()
        conversation = Conversation(user_id=user.id, title="测试会话")
        session.add(conversation)
        session.flush()
        session.add_all(
            [
                KnowledgeDocument(
                    title="员工请假政策",
                    category="leave_policy",
                    content="员工申请年假应至少提前三个工作日提交，并经直属主管审批。",
                    source_url="https://example.invalid/leave-policy",
                    enabled=True,
                ),
                KnowledgeDocument(
                    title="旧版请假政策",
                    category="leave_policy",
                    content="旧版规定年假提前一个工作日申请。",
                    enabled=False,
                ),
            ]
        )
        session.commit()
        yield session
    engine.dispose()


def test_mock_chat_returns_stable_leave_policy_answer_and_json_source(db: Session) -> None:
    provider = MockChatProvider(db)

    first = provider.chat("年假需要提前多久申请？", "employee-1", None)
    second = provider.chat("年假需要提前多久申请？", "employee-1", None)

    assert first == second
    assert "三个工作日" in first.answer
    assert "一个工作日" not in first.answer
    assert first.provider == "mock"
    assert first.model == "deterministic-knowledge-match"
    assert first.sources == [
        {
            "document_id": 1,
            "title": "员工请假政策",
            "category": "leave_policy",
            "source_url": "https://example.invalid/leave-policy",
        }
    ]
    assert first.raw_metadata["match_score"] > 0
    assert first.raw_metadata["trace_id"].startswith("mock-")
    assert first.raw_metadata["latency_ms"] == 0
    json.dumps(first.sources, ensure_ascii=False)
    json.dumps(first.raw_metadata, ensure_ascii=False)


def test_mock_chat_returns_transparent_answer_when_there_is_no_evidence(db: Session) -> None:
    result = MockChatProvider(db).chat("公司食堂今天供应什么甜点？", "employee-1", "local-1")

    assert result.answer == NO_EVIDENCE_ANSWER
    assert result.conversation_id == "local-1"
    assert result.sources == []
    assert result.raw_metadata["match_score"] == 0


def test_mock_contracts_are_synchronous_and_health_is_safe(db: Session) -> None:
    provider = MockChatProvider(db)
    tracer = MockTracer()

    assert isinstance(provider, ChatProvider)
    assert isinstance(tracer, Tracer)
    assert not inspect.iscoroutinefunction(provider.chat)
    assert not inspect.iscoroutinefunction(provider.health_check)
    assert not inspect.iscoroutinefunction(tracer.start_trace)
    assert provider.health_check().configured is True
    assert provider.health_check().reachable is True
    assert provider.health_check().error_code is None
    assert tracer.health_check().configured is True
    assert tracer.health_check().reachable is True
    assert tracer.health_check().error_code is None


def test_mock_trace_id_is_stable_and_records_are_no_ops() -> None:
    tracer = MockTracer()

    with tracer.start_trace("chat", "employee-1", "年假怎么申请？") as context:
        tracer.record_generation(context, "mock", "年假怎么申请？", "提前申请", 0, {"safe": True})
        tracer.record_score(context.trace_id, "helpful", 1.0, "有帮助")

    with tracer.start_trace("chat", "employee-1", "年假怎么申请？") as repeated:
        assert repeated.trace_id == context.trace_id
        assert repeated.trace_id.startswith("mock-")
        assert repeated.provider == "mock"


def test_factories_fall_back_to_mock_when_credentials_are_missing(db: Session) -> None:
    settings = Settings(
        AI_PROVIDER="dify",
        DIFY_API_KEY="",
        OBSERVABILITY_PROVIDER="langfuse",
        LANGFUSE_PUBLIC_KEY="public-only",
        LANGFUSE_SECRET_KEY="",
    )

    assert isinstance(create_chat_provider(db, settings), MockChatProvider)
    assert isinstance(create_tracer(settings), MockTracer)


def test_factories_honor_explicit_mock_even_when_credentials_exist(db: Session) -> None:
    settings = Settings(
        AI_PROVIDER="mock",
        DIFY_API_KEY="must-not-be-exposed",
        OBSERVABILITY_PROVIDER="mock",
        LANGFUSE_PUBLIC_KEY="public",
        LANGFUSE_SECRET_KEY="must-not-be-exposed",
    )

    provider = create_chat_provider(db, settings)
    tracer = create_tracer(settings)

    assert isinstance(provider, MockChatProvider)
    assert isinstance(tracer, MockTracer)
    assert "must-not-be-exposed" not in repr(provider)
    assert "must-not-be-exposed" not in repr(tracer)


def test_ai_run_success_and_failure_are_persisted(db: Session) -> None:
    conversation = db.scalar(select(Conversation))
    message = Message(conversation_id=conversation.id, role="assistant", content="回答", provider="mock")
    db.add(message)
    db.commit()

    result = MockChatProvider(db).chat("年假需要提前多久申请？", "employee-1", None)
    result.raw_metadata.update(
        {
            "latency_ms": 7,
            "input_tokens": 11,
            "output_tokens": 12,
            "total_cost": 0.0,
        }
    )
    successful = record_ai_run(db, message.id, result)
    failed = record_ai_failure(db, message.id, "dify", "integration_timeout", 15000)

    saved_runs = db.scalars(select(AiRun).order_by(AiRun.id)).all()
    assert saved_runs == [successful, failed]
    assert successful.provider == "mock"
    assert successful.status == "success"
    assert successful.trace_id.startswith("mock-")
    assert successful.model == "deterministic-knowledge-match"
    assert successful.latency_ms == 7
    assert successful.input_tokens == 11
    assert successful.output_tokens == 12
    assert successful.total_cost == 0.0
    assert successful.error_code is None
    assert failed.provider == "dify"
    assert failed.status == "failed"
    assert failed.trace_id is None
    assert failed.model is None
    assert failed.latency_ms == 15000
    assert failed.error_code == "integration_timeout"
