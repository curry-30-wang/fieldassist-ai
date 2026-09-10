from __future__ import annotations

from sqlalchemy.orm import Session

from backend.app.config import Settings, get_settings
from backend.app.integrations.contracts import ChatProvider, Tracer
from backend.app.integrations.mock_ai import MockChatProvider
from backend.app.integrations.mock_trace import MockTracer


def create_chat_provider(db: Session, settings: Settings | None = None) -> ChatProvider:
    configured = settings or get_settings()
    provider = configured.ai_provider.casefold()
    if provider == "mock" or not configured.dify_api_key:
        return MockChatProvider(db)
    if provider != "dify":
        raise ValueError("不支持的 AI 服务提供方")

    from backend.app.integrations.dify import DifyClient

    return DifyClient(settings=configured)


def create_tracer(settings: Settings | None = None) -> Tracer:
    configured = settings or get_settings()
    provider = configured.observability_provider.casefold()
    credentials_complete = bool(configured.langfuse_public_key and configured.langfuse_secret_key)
    if provider == "mock" or not credentials_complete:
        return MockTracer()
    if provider != "langfuse":
        raise ValueError("不支持的可观测性服务提供方")

    from backend.app.integrations.langfuse import LangfuseTracer

    return LangfuseTracer(configured)
