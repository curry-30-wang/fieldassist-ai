from __future__ import annotations

import logging
import time
from dataclasses import dataclass
from typing import Any

from sqlalchemy import select
from sqlalchemy.orm import Session

from backend.app.config import get_settings
from backend.app.dependencies import SafeHTTPException
from backend.app.integrations.contracts import (
    ChatResult,
    IntegrationError,
)
from backend.app.integrations.factory import create_chat_provider, create_tracer
from backend.app.models import AiRun, Conversation, Message, User
from backend.app.services.ai_runs import record_ai_failure, record_ai_run


logger = logging.getLogger(__name__)
MAX_CONVERSATION_TITLE_LENGTH = 255
MAX_QUESTION_LENGTH = 2000


@dataclass
class ChatServiceResult:
    user_message: Message
    assistant_message: Message | None
    sources: list[dict[str, Any]]
    provider: str
    ai_run: AiRun


def _clean_text(value: str, name: str, limit: int) -> str:
    cleaned = value.strip()
    if not cleaned:
        raise SafeHTTPException(status_code=422, detail=f"{name}不能为空")
    if len(cleaned) > limit:
        raise SafeHTTPException(
            status_code=422,
            detail=f"{name}长度不能超过{limit}个字符",
        )
    return cleaned


def create_conversation(db: Session, user: User, title: str) -> Conversation:
    conversation = Conversation(
        user_id=user.id,
        title=_clean_text(title, "会话标题", MAX_CONVERSATION_TITLE_LENGTH),
    )
    db.add(conversation)
    db.commit()
    db.refresh(conversation)
    return conversation


def _owned_conversation(db: Session, user: User, conversation_id: int) -> Conversation:
    conversation = db.scalar(
        select(Conversation).where(
            Conversation.id == conversation_id,
            Conversation.user_id == user.id,
        )
    )
    if conversation is None:
        raise SafeHTTPException(status_code=404, detail="会话不存在")
    return conversation


def _latency_ms(started_at: float) -> int:
    return max(0, int((time.perf_counter() - started_at) * 1000))


def _provider_name(provider: object, configured_name: str) -> str:
    name = getattr(provider, "provider", None)
    if isinstance(name, str) and name.strip():
        return name.strip()
    return configured_name.casefold()


def _safe_integration_error(error: Exception) -> tuple[str, str]:
    if isinstance(error, IntegrationError):
        return error.error_code, error.safe_message
    return IntegrationError.error_code, IntegrationError.safe_message


def send_message(
    db: Session,
    user: User,
    conversation_id: int,
    question: str,
) -> ChatServiceResult:
    conversation = _owned_conversation(db, user, conversation_id)
    cleaned_question = _clean_text(question, "问题", MAX_QUESTION_LENGTH)

    user_message = Message(
        conversation_id=conversation.id,
        role="user",
        content=cleaned_question,
        sources_json="[]",
    )
    db.add(user_message)
    db.commit()
    db.refresh(user_message)

    settings = get_settings()
    provider = create_chat_provider(db, settings)
    provider_name = _provider_name(provider, settings.ai_provider)
    tracer = create_tracer(settings)
    started_at = time.perf_counter()

    try:
        with tracer.start_trace(
            "chat",
            str(user.id),
            cleaned_question,
        ) as trace_context:
            result: ChatResult = provider.chat(
                cleaned_question,
                str(user.id),
                conversation.dify_conversation_id,
            )
            latency_ms = _latency_ms(started_at)
            result.raw_metadata = {
                **result.raw_metadata,
                "trace_id": trace_context.trace_id,
                "latency_ms": latency_ms,
            }
            assistant_message = Message(
                conversation_id=conversation.id,
                role="assistant",
                content=result.answer,
                provider=result.provider,
                sources_json="[]",
            )
            db.add(assistant_message)
            returned_conversation_id = result.conversation_id
            if isinstance(returned_conversation_id, str) and returned_conversation_id.strip():
                conversation.dify_conversation_id = returned_conversation_id.strip()
            db.flush()
            ai_run = record_ai_run(db, assistant_message.id, result)
            try:
                tracer.record_generation(
                    trace_context,
                    result.model,
                    cleaned_question,
                    result.answer,
                    latency_ms,
                    result.raw_metadata,
                )
            except Exception:
                logger.warning("trace_generation_failed")

            return ChatServiceResult(
                user_message=user_message,
                assistant_message=assistant_message,
                sources=result.sources,
                provider=result.provider,
                ai_run=ai_run,
            )
    except Exception as error:
        db.rollback()
        error_code, safe_message = _safe_integration_error(error)
        failed_run = record_ai_failure(
            db,
            user_message.id,
            provider_name,
            error_code,
            _latency_ms(started_at),
        )
        del failed_run
        raise SafeHTTPException(status_code=502, detail=safe_message) from None
