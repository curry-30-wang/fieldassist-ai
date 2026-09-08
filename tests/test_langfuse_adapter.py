from __future__ import annotations

import logging
from typing import Any

import pytest

from backend.app.config import Settings
from backend.app.integrations.factory import create_tracer
from backend.app.integrations.mock_trace import MockTracer

try:
    from backend.app.integrations.langfuse import LangfuseTracer
except ModuleNotFoundError:
    LangfuseTracer = None  # type: ignore[assignment,misc]


class FakeObservation:
    def __init__(self, observation_id: str, trace_id: str) -> None:
        self.id = observation_id
        self.trace_id = trace_id
        self.update_calls: list[dict[str, Any]] = []
        self.ended = False

    def __enter__(self) -> FakeObservation:
        return self

    def __exit__(self, *_args: object) -> None:
        self.end()

    def update(self, **kwargs: Any) -> FakeObservation:
        self.update_calls.append(kwargs)
        return self

    def end(self) -> FakeObservation:
        self.ended = True
        return self


class FakeLangfuseClient:
    def __init__(self, *, auth_result: bool = True) -> None:
        self.auth_result = auth_result
        self.observation_calls: list[dict[str, Any]] = []
        self.observations: list[FakeObservation] = []
        self.score_calls: list[dict[str, Any]] = []
        self.flush_calls = 0
        self.auth_calls = 0

    def start_observation(self, **kwargs: Any) -> FakeObservation:
        self.observation_calls.append(kwargs)
        observation = FakeObservation(
            f"observation-{len(self.observations) + 1}",
            "a" * 32,
        )
        self.observations.append(observation)
        return observation

    def score(self, **kwargs: Any) -> None:
        self.score_calls.append(kwargs)

    def flush(self) -> None:
        self.flush_calls += 1

    def auth_check(self) -> bool:
        self.auth_calls += 1
        return self.auth_result


def _settings(**overrides: object) -> Settings:
    values: dict[str, object] = {
        "OBSERVABILITY_PROVIDER": "langfuse",
        "LANGFUSE_PUBLIC_KEY": "public-test-key",
        "LANGFUSE_SECRET_KEY": "secret-test-key",
        "LANGFUSE_BASE_URL": "https://langfuse.example",
        "REQUEST_TIMEOUT_SECONDS": 4,
    }
    values.update(overrides)
    return Settings(**values)


def _tracer(settings: Settings | None = None, client: Any | None = None) -> Any:
    assert LangfuseTracer is not None, "LangfuseTracer adapter is not implemented"
    return LangfuseTracer(settings or _settings(), client=client)


def test_factory_keeps_mock_fallback_when_langfuse_credentials_are_incomplete() -> None:
    settings = _settings(LANGFUSE_SECRET_KEY="")

    assert isinstance(create_tracer(settings), MockTracer)


def test_factory_constructs_langfuse_tracer_for_configured_provider() -> None:
    tracer = create_tracer(_settings())

    assert LangfuseTracer is not None
    assert isinstance(tracer, LangfuseTracer)
    assert "secret-test-key" not in repr(tracer)


def test_start_trace_creates_v4_root_observation_and_local_context() -> None:
    client = FakeLangfuseClient()
    tracer = _tracer(client=client)

    context = tracer.start_trace("chat.request", "employee-1", "年假怎么申请？")

    assert context.trace_id
    assert context.trace_id == client.observations[0].trace_id
    assert context.provider == "langfuse"
    assert client.observation_calls == [
        {
            "name": "chat.request",
            "as_type": "span",
            "input": "年假怎么申请？",
            "metadata": {"user_id": "employee-1"},
        }
    ]


def test_record_generation_uses_v4_generation_observation_and_flushes() -> None:
    client = FakeLangfuseClient()
    tracer = _tracer(client=client)
    context = tracer.start_trace("chat.request", "employee-1", "问题")

    tracer.record_generation(
        context,
        "dify-model",
        "问题",
        "回答",
        123,
        {"provider": "dify", "request_id": "local-request"},
    )

    assert client.observation_calls[1] == {
        "name": "chat.generation",
        "as_type": "generation",
        "input": "问题",
        "output": "回答",
        "model": "dify-model",
        "metadata": {
            "provider": "dify",
            "request_id": "local-request",
            "latency_ms": 123,
        },
        "trace_context": {"trace_id": context.trace_id},
    }
    assert client.observations[1].ended is True
    assert client.flush_calls == 1


def test_start_trace_uses_uuid_hex_when_root_observation_fails() -> None:
    class FailingStartClient(FakeLangfuseClient):
        def start_observation(self, **kwargs: Any) -> FakeObservation:
            raise RuntimeError("private sdk response")

    context = _tracer(client=FailingStartClient()).start_trace(
        "chat.request", "employee-1", "问题"
    )

    assert len(context.trace_id) == 32


def test_record_score_sanitizes_control_characters_and_bounds_comment() -> None:
    client = FakeLangfuseClient()
    tracer = _tracer(client=client)
    comment = "有帮助\r\n\t\x00" + ("很" * 600)

    tracer.record_score("trace-123", "helpful", 1.0, comment)

    sent_comment = client.score_calls[0]["comment"]
    assert sent_comment.startswith("有帮助很")
    assert len(sent_comment) == 500
    assert all(not character.isspace() for character in sent_comment)
    assert client.score_calls[0] == {
        "name": "helpful",
        "value": 1.0,
        "trace_id": "trace-123",
        "comment": sent_comment,
    }
    assert client.flush_calls == 1


def test_health_check_is_safe_for_unconfigured_and_reachable_clients() -> None:
    unconfigured = _tracer(
        _settings(LANGFUSE_PUBLIC_KEY="", LANGFUSE_SECRET_KEY=""),
        FakeLangfuseClient(),
    )
    configured = _tracer(client=FakeLangfuseClient(auth_result=True))

    assert unconfigured.health_check().configured is False
    assert unconfigured.health_check().reachable is False
    assert unconfigured.health_check().error_code == "not_configured"
    assert configured.health_check().configured is True
    assert configured.health_check().reachable is True
    assert configured.health_check().error_code is None


def test_sdk_and_flush_failures_are_swallowed_and_logged_without_exception_text(
    caplog: pytest.LogCaptureFixture,
) -> None:
    class FailingClient(FakeLangfuseClient):
        def start_observation(self, **kwargs: Any) -> FakeObservation:
            raise RuntimeError("private sdk response with secret-test-key")

        def score(self, **kwargs: Any) -> None:
            raise RuntimeError("private score response with secret-test-key")

        def flush(self) -> None:
            raise RuntimeError("private flush response with secret-test-key")

        def auth_check(self) -> bool:
            raise RuntimeError("private health response with secret-test-key")

    client = FailingClient()
    tracer = _tracer(client=client)

    with caplog.at_level(logging.WARNING):
        context = tracer.start_trace("chat.request", "employee-1", "问题")
        tracer.record_generation(context, None, "问题", "回答", 1, {})
        tracer.record_score(context.trace_id, "helpful", 0.0, "评论")
        health = tracer.health_check()

    assert context.provider == "langfuse"
    assert context.trace_id
    assert health.configured is True
    assert health.reachable is False
    assert health.error_code == "health_check_failed"
    messages = " ".join(record.getMessage() for record in caplog.records)
    assert "langfuse_trace_start_failed" in messages
    assert "langfuse_generation_failed" in messages
    assert "langfuse_score_failed" in messages
    assert "langfuse_health_check_failed" in messages
    assert "secret-test-key" not in messages
    assert "private sdk response" not in messages


def test_client_construction_failure_falls_back_to_local_context() -> None:
    def failing_factory(_settings: Settings) -> Any:
        raise RuntimeError("private constructor detail")

    tracer = _tracer(client=None)
    tracer = LangfuseTracer(_settings(), client_factory=failing_factory)

    context = tracer.start_trace("chat.request", "employee-1", "问题")
    health = tracer.health_check()

    assert context.provider == "langfuse"
    assert context.trace_id
    assert health == type(health)(True, False, "client_init_failed")
