from __future__ import annotations

import json
from dataclasses import dataclass, field
from types import TracebackType
from typing import Any, Protocol, runtime_checkable


class IntegrationError(RuntimeError):
    error_code = "integration_error"
    safe_message = "AI 服务调用失败，请稍后重试"

    def __init__(self) -> None:
        super().__init__(self.safe_message)


class IntegrationAuthError(IntegrationError):
    error_code = "integration_auth"
    safe_message = "AI 服务认证失败，请联系管理员检查配置"


class IntegrationTimeoutError(IntegrationError):
    error_code = "integration_timeout"
    safe_message = "AI 服务响应超时，请稍后重试"


class IntegrationUnavailableError(IntegrationError):
    error_code = "integration_unavailable"
    safe_message = "AI 服务暂时不可用，请稍后重试"


class IntegrationResponseError(IntegrationError):
    error_code = "integration_response"
    safe_message = "AI 服务返回了无法解析的响应，请稍后重试"


@dataclass
class ChatResult:
    answer: str
    conversation_id: str | None
    sources: list[dict[str, Any]]
    provider: str
    model: str | None
    raw_metadata: dict[str, Any] = field(default_factory=dict)

    def __post_init__(self) -> None:
        json.dumps(self.sources)
        json.dumps(self.raw_metadata)


@dataclass(frozen=True)
class HealthResult:
    configured: bool
    reachable: bool
    error_code: str | None = None


@dataclass(frozen=True)
class TraceContext:
    trace_id: str
    provider: str

    def __enter__(self) -> TraceContext:
        return self

    def __exit__(
        self,
        _exception_type: type[BaseException] | None,
        _exception: BaseException | None,
        _traceback: TracebackType | None,
    ) -> None:
        return None


@runtime_checkable
class ChatProvider(Protocol):
    def chat(
        self,
        question: str,
        user_id: str,
        conversation_id: str | None,
    ) -> ChatResult: ...

    def health_check(self) -> HealthResult: ...


@runtime_checkable
class Tracer(Protocol):
    def start_trace(self, name: str, user_id: str, input_text: str) -> TraceContext: ...

    def record_generation(
        self,
        context: TraceContext,
        model: str | None,
        input_text: str,
        output_text: str,
        latency_ms: int,
        metadata: dict[str, Any],
    ) -> None: ...

    def record_score(
        self,
        trace_id: str,
        name: str,
        value: float,
        comment: str | None,
    ) -> None: ...

    def health_check(self) -> HealthResult: ...


@runtime_checkable
class AutomationClient(Protocol):
    def create_external_ticket(self, ticket: Any) -> Any | None: ...

    def notify_ticket(self, ticket: Any) -> Any | None: ...


class NoopAutomationClient:
    """Local automation boundary; deliberately performs no network calls."""

    def create_external_ticket(self, ticket: Any) -> None:
        del ticket
        return None

    def notify_ticket(self, ticket: Any) -> None:
        del ticket
        return None
