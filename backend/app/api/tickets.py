from __future__ import annotations

from typing import Annotated, Any

from fastapi import APIRouter, Depends, Response, status
from sqlalchemy.orm import Session

from backend.app.database import get_db
from backend.app.dependencies import get_current_user
from backend.app.models import Feedback, Ticket, User
from backend.app.schemas.tickets import FeedbackCreate, TicketCreate, TicketData, TicketPatch
from backend.app.services.tickets import create_feedback, create_ticket, list_tickets, update_ticket


router = APIRouter(prefix="/api", tags=["tickets"])


def _feedback_payload(feedback: Feedback) -> dict[str, Any]:
    return {
        "id": feedback.id,
        "message_id": feedback.message_id,
        "user_id": feedback.user_id,
        "rating": bool(feedback.rating),
        "comment": feedback.comment,
        "created_at": feedback.created_at,
    }


def _ticket_payload(ticket: Ticket) -> dict[str, Any]:
    return TicketData.model_validate(ticket).model_dump(mode="json")


@router.post("/messages/{message_id}/feedback")
def post_feedback(
    message_id: int,
    payload: FeedbackCreate,
    user: Annotated[User, Depends(get_current_user)],
    db: Annotated[Session, Depends(get_db)],
    response: Response,
) -> dict[str, Any]:
    feedback, created = create_feedback(
        db,
        user,
        message_id,
        payload.rating,
        payload.comment,
    )
    response.status_code = status.HTTP_201_CREATED if created else status.HTTP_200_OK
    return {
        "success": True,
        "data": {"feedback": _feedback_payload(feedback)},
        "message": "反馈提交成功",
    }


@router.post("/messages/{message_id}/ticket", status_code=status.HTTP_201_CREATED)
def post_ticket(
    message_id: int,
    payload: TicketCreate,
    user: Annotated[User, Depends(get_current_user)],
    db: Annotated[Session, Depends(get_db)],
) -> dict[str, Any]:
    ticket = create_ticket(
        db,
        user,
        message_id,
        payload.title,
        payload.description,
        payload.priority,
    )
    return {
        "success": True,
        "data": {"ticket": _ticket_payload(ticket)},
        "message": "工单创建成功",
    }


@router.get("/tickets")
def get_tickets(
    user: Annotated[User, Depends(get_current_user)],
    db: Annotated[Session, Depends(get_db)],
) -> dict[str, Any]:
    return {
        "success": True,
        "data": {"tickets": [_ticket_payload(ticket) for ticket in list_tickets(db, user)]},
        "message": "获取工单列表成功",
    }


@router.patch("/tickets/{ticket_id}")
def patch_ticket(
    ticket_id: int,
    payload: TicketPatch,
    user: Annotated[User, Depends(get_current_user)],
    db: Annotated[Session, Depends(get_db)],
) -> dict[str, Any]:
    ticket = update_ticket(db, user, ticket_id, payload.model_dump(exclude_unset=True))
    return {
        "success": True,
        "data": {"ticket": _ticket_payload(ticket)},
        "message": "工单更新成功",
    }
