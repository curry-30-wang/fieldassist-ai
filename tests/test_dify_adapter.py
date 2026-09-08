import asyncio
import inspect
import json
from collections.abc import Callable

import httpx
import pytest
from sqlalchemy import create_engine
from sqlalchemy.orm import Session

from backend.app.config import Settings
from backend.app.integrations.contracts import (
    ChatProvider,
    IntegrationAuthError as ContractIntegrationAuthError,
    IntegrationError as ContractIntegrationError,
    IntegrationResponseError as ContractIntegrationResponseError,
    IntegrationTimeoutError as ContractIntegrationTimeoutError,
    IntegrationUnavailableError as ContractIntegrationUnavailableError,
)
from backend.app.integrations.dify import (
    DifyClient,
    IntegrationAuthError,
    IntegrationError,
    IntegrationResponseError,
    IntegrationTimeoutError,
    IntegrationUnavailableError,
)
from backend.app.integrations.factory import create_chat_provider


API_KEY = "test-dify-key"


class ClosingMockTransport(httpx.MockTransport):
    def __init__(self, handler: Callable[[httpx.Request], httpx.Response]) -> None:
        super().__init__(handler)
        self.closed = False

    async def aclose(self) -> None:
        self.closed = True
        await super().aclose()


def _settings(**overrides: object) -> Settings:
    values: dict[str, object] = {
        "AI_PROVIDER": "dify",
        "DIFY_BASE_URL": "https://dify.example",
        "DIFY_API_KEY": API_KEY,
        "DIFY_APP_ID": "fieldassist-app",
        "REQUEST_TIMEOUT_SECONDS": 3,
    }
    values.update(overrides)
    return Settings(**values)


def test_integration_errors_are_shared_contracts_and_dify_reexports_them() -> None:
    assert IntegrationError is ContractIntegrationError
    assert IntegrationAuthError is ContractIntegrationAuthError
    assert IntegrationTimeoutError is ContractIntegrationTimeoutError
    assert IntegrationUnavailableError is ContractIntegrationUnavailableError
    assert IntegrationResponseError is ContractIntegrationResponseError
    assert IntegrationAuthError().error_code == "integration_auth"
    assert str(IntegrationAuthError()) == "AI 服务认证失败，请联系管理员检查配置"


def test_chat_sends_blocking_request_and_extracts_answer_conversation_and_sources() -> None:
    captured: dict[str, object] = {}
    response_metadata = {
        "usage": {"prompt_tokens": 12, "completion_tokens": 7},
        "retriever_resources": [
            {
                "position": 1,
                "dataset_id": "dataset-1",
                "dataset_name": "员工手册",
                "document_id": "document-1",
                "document_name": "请假制度.md",
                "segment_id": "segment-1",
                "score": 0.91,
                "content": "年假至少提前三个工作日申请。",
            }
        ],
        "provider_detail": {"request_id": "request-1"},
    }

    def handler(request: httpx.Request) -> httpx.Response:
        captured["method"] = request.method
        captured["url"] = str(request.url)
        captured["authorization"] = request.headers.get("Authorization")
        captured["body"] = json.loads(request.content)
        return httpx.Response(
            200,
            json={
                "event": "message",
                "answer": "请至少提前三个工作日提交申请。",
                "conversation_id": "dify-conversation-1",
                "metadata": response_metadata,
            },
        )

    client = DifyClient(_settings(), transport=httpx.MockTransport(handler))

    result = client.chat("年假需要提前多久申请？", "employee-1", "existing-conversation")

    assert captured == {
        "method": "POST",
        "url": "https://dify.example/v1/chat-messages",
        "authorization": f"Bearer {API_KEY}",
        "body": {
            "inputs": {},
            "query": "年假需要提前多久申请？",
            "response_mode": "blocking",
            "user": "employee-1",
            "conversation_id": "existing-conversation",
        },
    }
    assert result.answer == "请至少提前三个工作日提交申请。"
    assert result.conversation_id == "dify-conversation-1"
    assert result.sources == response_metadata["retriever_resources"]
    assert result.provider == "dify"
    assert result.model is None
    assert result.raw_metadata == response_metadata


def test_chat_omits_conversation_id_when_starting_a_new_conversation() -> None:
    captured_body: dict[str, object] = {}

    def handler(request: httpx.Request) -> httpx.Response:
        captured_body.update(json.loads(request.content))
        return httpx.Response(200, json={"answer": "回答", "conversation_id": "new-id", "metadata": {}})

    DifyClient(_settings(), transport=httpx.MockTransport(handler)).chat("问题", "employee-1", None)

    assert "conversation_id" not in captured_body


@pytest.mark.parametrize("base_url", ["", "not-a-url"])
def test_chat_maps_empty_or_malformed_base_url_to_safe_response_error(base_url: str) -> None:
    def handler(_request: httpx.Request) -> httpx.Response:
        raise AssertionError("invalid base URL must not reach the transport")

    client = DifyClient(
        _settings(DIFY_BASE_URL=base_url),
        transport=httpx.MockTransport(handler),
    )

    with pytest.raises(IntegrationResponseError) as captured:
        client.chat("问题", "employee-1", None)

    assert captured.value.error_code == "integration_response"
    assert API_KEY not in str(captured.value)


def test_chat_uses_configured_timeout() -> None:
    captured_timeout: dict[str, float] = {}

    def handler(request: httpx.Request) -> httpx.Response:
        captured_timeout.update(request.extensions["timeout"])
        return httpx.Response(200, json={"answer": "回答", "conversation_id": "new-id", "metadata": {}})

    transport = httpx.MockTransport(handler)
    DifyClient(_settings(), transport=transport).chat("问题", "employee-1", None)

    assert captured_timeout == {
        "connect": 3,
        "read": 3,
        "write": 3,
        "pool": 3,
    }


def test_chat_closes_the_async_transport() -> None:
    def handler(_request: httpx.Request) -> httpx.Response:
        return httpx.Response(200, json={"answer": "回答", "conversation_id": "new-id", "metadata": {}})

    transport = ClosingMockTransport(handler)

    DifyClient(_settings(), transport=transport).chat("问题", "employee-1", None)

    assert transport.closed is True


def test_chat_can_run_at_a_synchronous_boundary_inside_an_active_event_loop() -> None:
    def handler(_request: httpx.Request) -> httpx.Response:
        return httpx.Response(200, json={"answer": "回答", "conversation_id": "conversation-1", "metadata": {}})

    client = DifyClient(_settings(), transport=httpx.MockTransport(handler))

    async def call_synchronous_contract() -> str:
        return client.chat("问题", "employee-1", None).answer

    assert asyncio.run(call_synchronous_contract()) == "回答"
    assert not inspect.iscoroutinefunction(client.chat)
    assert not inspect.iscoroutinefunction(client.health_check)


@pytest.mark.parametrize(
    ("status_code", "expected_exception", "expected_code"),
    [
        (401, IntegrationAuthError, "integration_auth"),
        (403, IntegrationAuthError, "integration_auth"),
        (408, IntegrationTimeoutError, "integration_timeout"),
        (500, IntegrationUnavailableError, "integration_unavailable"),
        (503, IntegrationUnavailableError, "integration_unavailable"),
    ],
)
def test_chat_maps_http_errors_without_exposing_response_body_or_key(
    status_code: int,
    expected_exception: type[Exception],
    expected_code: str,
) -> None:
    def handler(_request: httpx.Request) -> httpx.Response:
        return httpx.Response(status_code, text="sensitive upstream body")

    client = DifyClient(_settings(), transport=httpx.MockTransport(handler))

    with pytest.raises(expected_exception) as captured:
        client.chat("问题", "employee-1", None)

    assert getattr(captured.value, "error_code") == expected_code
    assert "sensitive upstream body" not in str(captured.value)
    assert API_KEY not in str(captured.value)


def test_chat_maps_transport_timeout_to_safe_timeout_error() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        raise httpx.ReadTimeout("timeout with sensitive details", request=request)

    client = DifyClient(_settings(), transport=httpx.MockTransport(handler))

    with pytest.raises(IntegrationTimeoutError) as captured:
        client.chat("问题", "employee-1", None)

    assert captured.value.error_code == "integration_timeout"
    assert "sensitive" not in str(captured.value)
    assert API_KEY not in str(captured.value)


def test_chat_maps_malformed_json_to_safe_response_error() -> None:
    def handler(_request: httpx.Request) -> httpx.Response:
        return httpx.Response(200, content=b"not-json")

    client = DifyClient(_settings(), transport=httpx.MockTransport(handler))

    with pytest.raises(IntegrationResponseError) as captured:
        client.chat("问题", "employee-1", None)

    assert captured.value.error_code == "integration_response"
    assert "not-json" not in str(captured.value)
    assert API_KEY not in str(captured.value)


@pytest.mark.parametrize(
    "response_payload",
    [
        {"answer": "回答", "metadata": {}},
        {"answer": "回答", "conversation_id": None, "metadata": {}},
        {"answer": "回答", "conversation_id": "", "metadata": {}},
        {"answer": "回答", "conversation_id": 7, "metadata": {}},
    ],
)
def test_chat_rejects_missing_null_empty_or_non_string_conversation_id(
    response_payload: dict[str, object],
) -> None:
    def handler(_request: httpx.Request) -> httpx.Response:
        return httpx.Response(200, json=response_payload)

    client = DifyClient(_settings(), transport=httpx.MockTransport(handler))

    with pytest.raises(IntegrationResponseError) as captured:
        client.chat("问题", "employee-1", None)

    assert captured.value.error_code == "integration_response"
    assert API_KEY not in str(captured.value)


def test_health_check_uses_parameters_endpoint_and_returns_safe_reachable_result() -> None:
    captured: dict[str, object] = {}

    def handler(request: httpx.Request) -> httpx.Response:
        captured["method"] = request.method
        captured["url"] = str(request.url)
        captured["authorization"] = request.headers.get("Authorization")
        return httpx.Response(200, json={"opening_statement": "hello"})

    result = DifyClient(_settings(), transport=httpx.MockTransport(handler)).health_check()

    assert captured == {
        "method": "GET",
        "url": "https://dify.example/v1/parameters?user=fieldassist-health",
        "authorization": f"Bearer {API_KEY}",
    }
    assert result.configured is True
    assert result.reachable is True
    assert result.error_code is None
    assert API_KEY not in repr(result)


def test_health_check_reports_missing_required_configuration_without_http_request() -> None:
    called = False

    def handler(_request: httpx.Request) -> httpx.Response:
        nonlocal called
        called = True
        return httpx.Response(200)

    result = DifyClient(
        _settings(DIFY_API_KEY=""),
        transport=httpx.MockTransport(handler),
    ).health_check()

    assert called is False
    assert result.configured is False
    assert result.reachable is False
    assert result.error_code == "not_configured"


@pytest.mark.parametrize(
    ("response_or_error", "expected_code"),
    [
        (httpx.Response(401, text="secret body"), "integration_auth"),
        (httpx.Response(408, text="secret body"), "integration_timeout"),
        (httpx.Response(503, text="secret body"), "integration_unavailable"),
        (httpx.Response(400, text="secret body"), "integration_response"),
        (httpx.ConnectError("secret network detail"), "integration_unavailable"),
    ],
)
def test_health_check_returns_safe_error_codes(
    response_or_error: httpx.Response | Exception,
    expected_code: str,
) -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        if isinstance(response_or_error, httpx.RequestError):
            response_or_error.request = request
            raise response_or_error
        return response_or_error

    result = DifyClient(_settings(), transport=httpx.MockTransport(handler)).health_check()

    assert result.configured is True
    assert result.reachable is False
    assert result.error_code == expected_code
    assert API_KEY not in repr(result)


def test_factory_creates_dify_client_for_real_mode() -> None:
    engine = create_engine("sqlite:///:memory:")
    try:
        with Session(engine) as db:
            provider = create_chat_provider(db, _settings())
    finally:
        engine.dispose()

    assert isinstance(provider, DifyClient)
    assert isinstance(provider, ChatProvider)
    assert API_KEY not in repr(provider)
