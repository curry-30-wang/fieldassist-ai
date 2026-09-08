from __future__ import annotations

from typing import Any

from sqlalchemy import func, select, text
from sqlalchemy.orm import Session

from backend.app.config import Settings
from backend.app.integrations.contracts import HealthResult
from backend.app.integrations.factory import create_chat_provider, create_tracer
from backend.app.models import AiRun, Feedback, KnowledgeDocument, Message, Ticket


def _health_payload(result: HealthResult) -> dict[str, Any]:
    return {
        "configured": result.configured,
        "reachable": result.reachable,
        "error_code": result.error_code,
    }


def get_summary(db: Session) -> dict[str, Any]:
    total_questions = int(
        db.scalar(select(func.count(Message.id)).where(Message.role == "user")) or 0
    )
    average_latency = db.scalar(
        select(func.avg(AiRun.latency_ms)).where(
            AiRun.status == "success",
            AiRun.latency_ms.is_not(None),
        )
    )
    feedback_total = int(db.scalar(select(func.count(Feedback.id))) or 0)
    positive_feedback = int(
        db.scalar(select(func.count(Feedback.id)).where(Feedback.rating == 1)) or 0
    )
    open_ticket_count = int(
        db.scalar(
            select(func.count(Ticket.id)).where(
                Ticket.status.in_(("open", "in_progress"))
            )
        )
        or 0
    )
    provider_rows = db.execute(
        select(AiRun.provider, func.count(AiRun.id))
        .group_by(AiRun.provider)
        .order_by(AiRun.provider)
    ).all()

    return {
        "total_questions": total_questions,
        "average_latency_ms": int(round(float(average_latency))) if average_latency is not None else 0,
        "positive_feedback_rate": positive_feedback / feedback_total if feedback_total else 0,
        "open_ticket_count": open_ticket_count,
        "provider_breakdown": {str(provider): int(count) for provider, count in provider_rows},
    }


def get_integration_health(db: Session, settings: Settings) -> dict[str, Any]:
    application = HealthResult(configured=True, reachable=True)
    database = HealthResult(configured=True, reachable=True)
    try:
        db.execute(text("SELECT 1"))
    except Exception:
        db.rollback()
        database = HealthResult(
            configured=True,
            reachable=False,
            error_code="database_unavailable",
        )

    try:
        ai_result = create_chat_provider(db, settings).health_check()
    except Exception:
        ai_result = HealthResult(
            configured=False,
            reachable=False,
            error_code="ai_provider_error",
        )

    try:
        observability_result = create_tracer(settings).health_check()
    except Exception:
        observability_result = HealthResult(
            configured=False,
            reachable=False,
            error_code="observability_provider_error",
        )

    return {
        "application": _health_payload(application),
        "ai_provider": _health_payload(ai_result),
        "observability_provider": _health_payload(observability_result),
        "database": _health_payload(database),
    }


def list_enabled_documents(db: Session) -> list[dict[str, Any]]:
    documents = db.scalars(
        select(KnowledgeDocument)
        .where(KnowledgeDocument.enabled.is_(True))
        .order_by(KnowledgeDocument.id)
    ).all()
    return [
        {
            "id": document.id,
            "title": document.title,
            "category": document.category,
            "source_url": document.source_url,
            "enabled": document.enabled,
            "created_at": document.created_at,
        }
        for document in documents
    ]
