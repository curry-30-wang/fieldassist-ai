from __future__ import annotations

import asyncio
from collections.abc import Awaitable, Callable
from concurrent.futures import ThreadPoolExecutor
from typing import Any, TypeVar
from urllib.parse import urlsplit

import httpx

from backend.app.config import Settings
from backend.app.integrations.contracts import (
    ChatResult,
    HealthResult,
    IntegrationAuthError,
    IntegrationError,
    IntegrationResponseError,
    IntegrationTimeoutError,
    IntegrationUnavailableError,
)


_T = TypeVar("_T")


def _run_synchronously(operation: Callable[[], Awaitable[_T]]) -> _T:
    try:
        asyncio.get_running_loop()
    except RuntimeError:
        return asyncio.run(operation())

    with ThreadPoolExecutor(max_workers=1) as executor:
        return executor.submit(asyncio.run, operation()).result()


def _api_base_url(configured_base_url: str) -> str:
    base_url = configured_base_url.strip().rstrip("/")
    parsed = urlsplit(base_url)
    if parsed.scheme.casefold() not in {"http", "https"} or not parsed.netloc:
        raise IntegrationResponseError
    if base_url.casefold().endswith("/v1"):
        return base_url
    return f"{base_url}/v1"


def _raise_for_status(response: httpx.Response) -> None:
    if 200 <= response.status_code < 300:
        return
    if response.status_code in {401, 403}:
        raise IntegrationAuthError
    if response.status_code == 408:
        raise IntegrationTimeoutError
    if 500 <= response.status_code < 600:
        raise IntegrationUnavailableError
    raise IntegrationResponseError


class DifyClient:
    def __init__(
        self,
        settings: Settings,
        transport: httpx.AsyncBaseTransport | None = None,
    ) -> None:
        self._base_url = settings.dify_base_url.strip()
        self._api_key = settings.dify_api_key.strip()
        self._timeout_seconds = settings.request_timeout_seconds
        self._transport = transport

    def _client(self) -> httpx.AsyncClient:
        try:
            return httpx.AsyncClient(
                base_url=f"{_api_base_url(self._base_url)}/",
                headers={"Authorization": f"Bearer {self._api_key}"},
                timeout=self._timeout_seconds,
                transport=self._transport,
            )
        except (TypeError, ValueError):
            raise IntegrationResponseError from None

    def chat(
        self,
        question: str,
        user_id: str,
        conversation_id: str | None,
    ) -> ChatResult:
        return _run_synchronously(
            lambda: self._chat(question, user_id, conversation_id)
        )

    async def _chat(
        self,
        question: str,
        user_id: str,
        conversation_id: str | None,
    ) -> ChatResult:
        body: dict[str, Any] = {
            "inputs": {},
            "query": question,
            "response_mode": "blocking",
            "user": user_id,
        }
        if conversation_id is not None:
            body["conversation_id"] = conversation_id

        try:
            async with self._client() as client:
                response = await client.post("chat-messages", json=body)
        except httpx.TimeoutException:
            raise IntegrationTimeoutError from None
        except httpx.RequestError:
            raise IntegrationUnavailableError from None

        _raise_for_status(response)
        try:
            payload = response.json()
        except ValueError:
            raise IntegrationResponseError from None

        if not isinstance(payload, dict):
            raise IntegrationResponseError
        answer = payload.get("answer")
        returned_conversation_id = payload.get("conversation_id")
        metadata = payload.get("metadata", {})
        if not isinstance(answer, str):
            raise IntegrationResponseError
        if not isinstance(returned_conversation_id, str) or not returned_conversation_id.strip():
            raise IntegrationResponseError
        if not isinstance(metadata, dict):
            raise IntegrationResponseError

        retriever_resources = metadata.get("retriever_resources", [])
        if not isinstance(retriever_resources, list) or not all(
            isinstance(source, dict) for source in retriever_resources
        ):
            raise IntegrationResponseError

        model = payload.get("model")
        if not isinstance(model, str):
            model = metadata.get("model_name")
        if not isinstance(model, str):
            model = None

        return ChatResult(
            answer=answer,
            conversation_id=returned_conversation_id,
            sources=retriever_resources,
            provider="dify",
            model=model,
            raw_metadata=metadata,
        )

    def health_check(self) -> HealthResult:
        if not self._base_url or not self._api_key:
            return HealthResult(
                configured=False,
                reachable=False,
                error_code="not_configured",
            )
        return _run_synchronously(self._health_check)

    async def _health_check(self) -> HealthResult:
        try:
            async with self._client() as client:
                response = await client.get(
                    "parameters",
                    params={"user": "fieldassist-health"},
                )
            _raise_for_status(response)
        except IntegrationError as error:
            return HealthResult(
                configured=True,
                reachable=False,
                error_code=error.error_code,
            )
        except httpx.TimeoutException:
            return HealthResult(
                configured=True,
                reachable=False,
                error_code=IntegrationTimeoutError.error_code,
            )
        except httpx.RequestError:
            return HealthResult(
                configured=True,
                reachable=False,
                error_code=IntegrationUnavailableError.error_code,
            )

        return HealthResult(configured=True, reachable=True)
