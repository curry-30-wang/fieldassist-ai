from __future__ import annotations

from datetime import datetime

from pydantic import BaseModel


class HealthStatus(BaseModel):
    configured: bool
    reachable: bool
    error_code: str | None = None


class AdminSummary(BaseModel):
    total_questions: int
    average_latency_ms: int
    positive_feedback_rate: float
    open_ticket_count: int
    provider_breakdown: dict[str, int]


class DocumentMetadata(BaseModel):
    id: int
    title: str
    category: str
    source_url: str | None
    enabled: bool
    created_at: datetime
