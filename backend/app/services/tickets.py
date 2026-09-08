from __future__ import annotations

import logging

from sqlalchemy import select
from sqlalchemy.orm import Session

from backend.app.config import get_settings
from backend.app.dependencies import SafeHTTPException
from backend.app.integrations.factory import create_tracer
from backend.app.models import AiRun, Conversation, Feedback, Message, Ticket, User, utc_now


logger = logging.getLogger(__name__)

STATUS_TRANSITIONS: dict[str, set[str]] = {
    "open": {"open", "in_progress"},
    "in_progress": {"in_progress", "resolved"},
    "resolved": {"resolved", "closed"},
    "closed": {"closed"},
}


def _assistant_message_for_user(
    db: Session,
    message_id: int,
    user: User,
) -> Message:
    query = (
        select(Message)
        .join(Conversation, Message.conversation_id == Conversation.id)
        .where(Message.id == message_id, Message.role == "assistant")
    )
    if user.role != "admin":
        query = query.where(Conversation.user_id == user.id)
    message = db.scalar(query)
    if message is None:
        raise SafeHTTPException(status_code=404, detail="助手消息不存在")
    return message


def _record_feedback_score(message: Message, feedback: Feedback) -> None:
    try:
        trace_id = None
        for run in sorted(message.ai_runs, key=lambda item: item.id, reverse=True):
            if isinstance(run.trace_id, str) and run.trace_id:
                trace_id = run.trace_id
                break
        if trace_id is None:
            return
        tracer = create_tracer(get_settings())
        tracer.record_score(
            trace_id,
            "helpful",
            float(feedback.rating),
            feedback.comment,
        )
    except Exception:
        logger.warning("feedback_score_failed")


def create_feedback(
    db: Session,
    user: User,
    message_id: int,
    rating: bool,
    comment: str | None,
) -> tuple[Feedback, bool]:
    message = _assistant_message_for_user(db, message_id, user)
    feedback = db.scalar(
        select(Feedback).where(
            Feedback.message_id == message.id,
            Feedback.user_id == user.id,
        )
    )
    created = feedback is None
    if feedback is None:
        feedback = Feedback(message_id=message.id, user_id=user.id, rating=int(rating), comment=comment)
        db.add(feedback)
    else:
        feedback.rating = int(rating)
        feedback.comment = comment
    db.commit()
    db.refresh(feedback)
    _record_feedback_score(message, feedback)
    return feedback, created


def create_ticket(
    db: Session,
    user: User,
    message_id: int,
    title: str,
    description: str,
    priority: str,
) -> Ticket:
    message = _assistant_message_for_user(db, message_id, user)
    ticket = Ticket(
        source_message_id=message.id,
        created_by=user.id,
        title=title,
        description=description,
        priority=priority,
        status="open",
    )
    db.add(ticket)
    db.commit()
    db.refresh(ticket)
    return ticket


def list_tickets(db: Session, user: User) -> list[Ticket]:
    query = select(Ticket).order_by(Ticket.created_at.desc(), Ticket.id.desc())
    if user.role != "admin":
        query = query.where(Ticket.created_by == user.id)
    return db.scalars(query).all()


def update_ticket(
    db: Session,
    user: User,
    ticket_id: int,
    changes: dict[str, object],
) -> Ticket:
    query = select(Ticket).where(Ticket.id == ticket_id)
    if user.role != "admin":
        query = query.where(Ticket.created_by == user.id)
    ticket = db.scalar(query)
    if ticket is None:
        raise SafeHTTPException(status_code=404, detail="工单不存在")

    new_status = changes.get("status")
    if isinstance(new_status, str) and new_status not in STATUS_TRANSITIONS.get(ticket.status, set()):
        raise SafeHTTPException(status_code=422, detail="不允许的工单状态流转")
    for field in ("title", "description", "priority", "status"):
        if field in changes and changes[field] is not None:
            setattr(ticket, field, changes[field])
    ticket.updated_at = utc_now()
    db.commit()
    db.refresh(ticket)
    return ticket
