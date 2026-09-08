from __future__ import annotations

from datetime import datetime
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, field_validator


TicketPriority = Literal["low", "medium", "high", "urgent"]
TicketStatus = Literal["open", "in_progress", "resolved", "closed"]


def _trim_and_validate(value: str, field_name: str, limit: int) -> str:
    cleaned = value.strip()
    if not cleaned:
        raise ValueError(f"{field_name}不能为空")
    if len(cleaned) > limit:
        raise ValueError(f"{field_name}长度不能超过{limit}个字符")
    return cleaned


class FeedbackCreate(BaseModel):
    rating: bool
    comment: str | None = Field(default=None, max_length=500)

    @field_validator("comment")
    @classmethod
    def validate_comment(cls, value: str | None) -> str | None:
        if value is None:
            return None
        return _trim_and_validate(value, "反馈说明", 500)


class TicketCreate(BaseModel):
    title: str = Field(min_length=1, max_length=255)
    description: str = Field(min_length=1, max_length=2000)
    priority: TicketPriority

    @field_validator("title")
    @classmethod
    def validate_title(cls, value: str) -> str:
        return _trim_and_validate(value, "工单标题", 255)

    @field_validator("description")
    @classmethod
    def validate_description(cls, value: str) -> str:
        return _trim_and_validate(value, "工单描述", 2000)


class TicketPatch(BaseModel):
    title: str | None = Field(default=None, min_length=1, max_length=255)
    description: str | None = Field(default=None, min_length=1, max_length=2000)
    priority: TicketPriority | None = None
    status: TicketStatus | None = None

    @field_validator("title")
    @classmethod
    def validate_title(cls, value: str | None) -> str | None:
        if value is None:
            return None
        return _trim_and_validate(value, "工单标题", 255)

    @field_validator("description")
    @classmethod
    def validate_description(cls, value: str | None) -> str | None:
        if value is None:
            return None
        return _trim_and_validate(value, "工单描述", 2000)


class FeedbackData(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: int
    message_id: int
    user_id: int
    rating: bool
    comment: str | None = None
    created_at: datetime


class TicketData(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: int
    source_message_id: int
    created_by: int
    title: str
    description: str
    priority: TicketPriority
    status: TicketStatus
    created_at: datetime
    updated_at: datetime
