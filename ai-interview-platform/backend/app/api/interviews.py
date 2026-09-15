from typing import Annotated

from fastapi import APIRouter, File, Form, Request, UploadFile, status

from app.models import InterviewSession, Question
from app.services.interview_service import InterviewService


router = APIRouter(prefix="/api/interviews", tags=["interviews"])


def _service(request: Request) -> InterviewService:
    return request.app.state.interview_service


def _question_payload(question: Question) -> dict:
    import json

    return {
        "id": question.id,
        "session_id": question.session_id,
        "question_text": question.question_text,
        "question_type": question.question_type,
        "difficulty": question.difficulty,
        "focus_points": json.loads(question.focus_points),
        "reference_direction": question.reference_direction,
        "order_index": question.order_index,
    }


def _session_payload(interview: InterviewSession) -> dict:
    return {
        "id": interview.id,
        "job_title": interview.job_title,
        "job_description": interview.job_description,
        "resume_document_id": interview.resume_document_id,
        "status": interview.status,
        "created_at": interview.created_at,
        "updated_at": interview.updated_at,
    }


@router.post("", status_code=status.HTTP_201_CREATED)
async def create_interview(
    request: Request,
    job_description: Annotated[str, Form()],
    resume: Annotated[UploadFile, File()],
) -> dict:
    data = await resume.read()
    if len(data) > request.app.state.settings.max_upload_mb * 1024 * 1024:
        from app.services.interview_service import InvalidInterviewInputError

        raise InvalidInterviewInputError("resume file is too large")
    interview = await _service(request).create_session(
        job_description,
        resume.filename or "resume",
        resume.content_type or "application/octet-stream",
        data,
    )
    return _session_payload(interview)


@router.get("")
def list_interviews(request: Request) -> dict:
    return {
        "interviews": [
            _session_payload(interview)
            for interview in _service(request).list_sessions()
        ]
    }


@router.get("/{session_id}")
def get_interview(session_id: str, request: Request) -> dict:
    interview, questions = _service(request).get_session(session_id)
    return {
        **_session_payload(interview),
        "questions": [_question_payload(question) for question in questions],
    }


@router.post("/{session_id}/questions")
async def generate_questions(session_id: str, request: Request) -> dict:
    questions = await _service(request).generate_questions(session_id)
    return {"questions": [_question_payload(question) for question in questions]}


@router.get("/{session_id}/report")
async def get_report(session_id: str, request: Request) -> dict:
    report = await _service(request).get_report(session_id)
    return report.model_dump()
