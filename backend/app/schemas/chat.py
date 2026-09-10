from __future__ import annotations

from datetime import datetime
from typing import Any

from pydantic import BaseModel, ConfigDict, Field, field_validator


def _trim_and_validate(value: str, field_name: str, limit: int) -> str:
    value = value.strip()
    if not value:
        raise ValueError(f"{field_name}不能为空")
    if len(value) > limit:
        raise ValueError(f"{field_name}长度不能超过{limit}个字符")
    return value


class ConversationCreate(BaseModel):
    title: str = Field(min_length=1, max_length=255)

    @field_validator("title")
    @classmethod
    def validate_title(cls, value: str) -> str:
        return _trim_and_validate(value, "会话标题", 255)


class MessageCreate(BaseModel):
    question: str = Field(min_length=1, max_length=2000)

    @field_validator("question")
    @classmethod
    def validate_question(cls, value: str) -> str:
        return _trim_and_validate(value, "问题", 2000)


class MessageData(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: int
    role: str
    content: str
    provider: str | None = None
    sources: list[dict[str, Any]] = Field(default_factory=list)
    created_at: datetime
    ai_run: dict[str, Any] | None = None


class ConversationSummary(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: int
    title: str
    dify_conversation_id: str | None = None
    created_at: datetime


class ConversationDetail(ConversationSummary):
    user_id: int
    messages: list[MessageData] = Field(default_factory=list)
