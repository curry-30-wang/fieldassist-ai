from __future__ import annotations

import json
from typing import Annotated, Any

from fastapi import APIRouter, Depends, status
from sqlalchemy import func, select
from sqlalchemy.orm import Session

from backend.app.database import get_db
from backend.app.dependencies import get_current_user
from backend.app.models import AiRun, Conversation, Message, User
from backend.app.schemas.chat import ConversationCreate, MessageCreate
from backend.app.services.chat import (
    ChatServiceResult,
    create_conversation,
    send_message,
)


router = APIRouter(prefix="/api/conversations", tags=["conversations"])


def _sources(message: Message) -> list[dict[str, Any]]:
    try:
        value = json.loads(message.sources_json or "[]")
    except (TypeError, ValueError):
        return []
    return value if isinstance(value, list) else []


def _ai_run_payload(run: AiRun | None) -> dict[str, Any] | None:
    if run is None:
        return None
    return {
        "id": run.id,
        "provider": run.provider,
        "status": run.status,
        "trace_id": run.trace_id,
        "model": run.model,
        "latency_ms": run.latency_ms,
        "input_tokens": run.input_tokens,
        "output_tokens": run.output_tokens,
        "total_cost": run.total_cost,
        "error_code": run.error_code,
        "created_at": run.created_at,
    }


def _message_payload(message: Message, include_ai_run: bool = True) -> dict[str, Any]:
    run = None
    if include_ai_run:
        run = max(message.ai_runs, key=lambda item: item.id, default=None)
    return {
        "id": message.id,
        "role": message.role,
        "content": message.content,
        "provider": message.provider,
        "sources": _sources(message),
        "created_at": message.created_at,
        "ai_run": _ai_run_payload(run),
    }


def _conversation_summary(conversation: Conversation) -> dict[str, Any]:
    return {
        "id": conversation.id,
        "user_id": conversation.user_id,
        "title": conversation.title,
        "dify_conversation_id": conversation.dify_conversation_id,
        "created_at": conversation.created_at,
    }


def _conversation_detail(
    conversation: Conversation,
    messages: list[Message],
) -> dict[str, Any]:
    return {
        **_conversation_summary(conversation),
        "messages": [_message_payload(message) for message in messages],
    }


def _chat_result_payload(result: ChatServiceResult) -> dict[str, Any]:
    assistant = result.assistant_message
    return {
        "user_message": _message_payload(result.user_message, include_ai_run=False),
        "assistant_message": _message_payload(assistant) if assistant is not None else None,
        "sources": result.sources,
        "provider": result.provider,
        "ai_run": _ai_run_payload(result.ai_run),
    }


@router.post("", status_code=status.HTTP_201_CREATED)
def create_conversation_route(
    payload: ConversationCreate,
    user: Annotated[User, Depends(get_current_user)],
    db: Annotated[Session, Depends(get_db)],
) -> dict[str, Any]:
    conversation = create_conversation(db, user, payload.title)
    return {
        "success": True,
        "data": {"conversation": _conversation_summary(conversation)},
        "message": "会话创建成功",
    }


@router.get("")
def list_conversations(
    user: Annotated[User, Depends(get_current_user)],
    db: Annotated[Session, Depends(get_db)],
) -> dict[str, Any]:
    activity = func.coalesce(func.max(Message.created_at), Conversation.created_at)
    conversations = db.scalars(
        select(Conversation)
        .outerjoin(Message, Message.conversation_id == Conversation.id)
        .where(Conversation.user_id == user.id)
        .group_by(Conversation.id, Conversation.created_at)
        .order_by(activity.desc(), Conversation.id.desc())
    ).all()
    return {
        "success": True,
        "data": {
            "conversations": [_conversation_summary(item) for item in conversations],
        },
        "message": "获取会话列表成功",
    }


@router.get("/{conversation_id}")
def get_conversation(
    conversation_id: int,
    user: Annotated[User, Depends(get_current_user)],
    db: Annotated[Session, Depends(get_db)],
) -> dict[str, Any]:
    conversation = db.scalar(
        select(Conversation).where(
            Conversation.id == conversation_id,
            Conversation.user_id == user.id,
        )
    )
    if conversation is None:
        from backend.app.dependencies import SafeHTTPException

        raise SafeHTTPException(status_code=404, detail="会话不存在")
    messages = db.scalars(
        select(Message)
        .where(Message.conversation_id == conversation.id)
        .order_by(Message.created_at.asc(), Message.id.asc())
    ).all()
    return {
        "success": True,
        "data": {"conversation": _conversation_detail(conversation, messages)},
        "message": "获取会话详情成功",
    }


@router.post("/{conversation_id}/messages")
def post_message(
    conversation_id: int,
    payload: MessageCreate,
    user: Annotated[User, Depends(get_current_user)],
    db: Annotated[Session, Depends(get_db)],
) -> dict[str, Any]:
    result = send_message(db, user, conversation_id, payload.question)
    return {
        "success": True,
        "data": _chat_result_payload(result),
        "message": "消息发送成功",
    }
