from fastapi import APIRouter, Request, status
from pydantic import BaseModel, ConfigDict

from app.services.interview_service import InterviewService


router = APIRouter(prefix="/api/questions", tags=["questions"])


class AnswerRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    answer_text: str


def _service(request: Request) -> InterviewService:
    return request.app.state.interview_service


@router.post("/{question_id}/answers", status_code=status.HTTP_201_CREATED)
async def submit_answer(
    question_id: str, payload: AnswerRequest, request: Request
) -> dict:
    evaluation = await _service(request).submit_answer(
        question_id, payload.answer_text
    )
    result = evaluation.model_dump()
    result["score"]["total_score"] = evaluation.score.total_score
    return result
