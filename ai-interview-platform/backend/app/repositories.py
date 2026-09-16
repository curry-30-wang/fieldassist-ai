import json
from collections.abc import Sequence

from sqlalchemy import delete, select
from sqlalchemy.orm import Session

from app.models import (
    Answer,
    Document,
    DocumentChunk,
    InterviewSession,
    OperationClaim,
    Question,
    Report,
)
from app.schemas import (
    AnswerEvaluation,
    GeneratedQuestion,
    InterviewReport,
    ReportQuestion,
    ReportResult,
    ScoreBreakdown,
)


class InterviewRepository:
    def __init__(self, db: Session) -> None:
        self.db = db

    def create(self, job_title: str, job_description: str) -> InterviewSession:
        interview = InterviewSession(job_title=job_title, job_description=job_description)
        self.db.add(interview)
        self.db.flush()
        return interview

    def get(self, session_id: str) -> InterviewSession | None:
        return self.db.get(InterviewSession, session_id)

    def list(self) -> list[InterviewSession]:
        return list(
            self.db.scalars(
                select(InterviewSession).order_by(InterviewSession.created_at.desc())
            )
        )


class DocumentRepository:
    def __init__(self, db: Session) -> None:
        self.db = db

    def create(
        self,
        filename: str,
        content_type: str,
        text: str,
        chunks: Sequence[str],
    ) -> Document:
        document = Document(
            filename=filename,
            content_type=content_type,
            text_content=text,
        )
        self.db.add(document)
        self.db.flush()
        self.db.add_all(
            DocumentChunk(
                document_id=document.id,
                chunk_index=index,
                content=chunk,
                keywords="",
            )
            for index, chunk in enumerate(chunks)
        )
        return document

    def chunks(self, document_id: str | None) -> list[DocumentChunk]:
        if document_id is None:
            return []
        return list(
            self.db.scalars(
                select(DocumentChunk)
                .where(DocumentChunk.document_id == document_id)
                .order_by(DocumentChunk.chunk_index)
            )
        )


class QuestionRepository:
    def __init__(self, db: Session) -> None:
        self.db = db

    def create_many(
        self, session_id: str, generated: Sequence[GeneratedQuestion]
    ) -> list[Question]:
        questions = [
            Question(
                session_id=session_id,
                question_text=item.question_text,
                question_type=item.question_type,
                difficulty=item.difficulty,
                focus_points=json.dumps(item.focus_points, ensure_ascii=False),
                reference_direction=item.reference_direction,
                order_index=index,
            )
            for index, item in enumerate(generated)
        ]
        self.db.add_all(questions)
        self.db.flush()
        return questions

    def get(self, question_id: str) -> Question | None:
        return self.db.get(Question, question_id)

    def list_for_session(self, session_id: str) -> list[Question]:
        return list(
            self.db.scalars(
                select(Question)
                .where(Question.session_id == session_id)
                .order_by(Question.order_index)
            )
        )


class AnswerRepository:
    def __init__(self, db: Session) -> None:
        self.db = db

    def get_for_question(self, question_id: str) -> Answer | None:
        return self.db.scalar(select(Answer).where(Answer.question_id == question_id))

    def create(
        self, question_id: str, answer_text: str, evaluation: AnswerEvaluation
    ) -> Answer:
        answer = Answer(
            question_id=question_id,
            answer_text=answer_text,
            score_json=json.dumps(
                evaluation.score.model_dump(exclude={"total_score"}),
                ensure_ascii=False,
            ),
            feedback_json=json.dumps(
                {
                    "strengths": evaluation.strengths,
                    "problems": evaluation.problems,
                    "suggestions": evaluation.suggestions,
                    "answer_structure": evaluation.answer_structure,
                },
                ensure_ascii=False,
            ),
        )
        self.db.add(answer)
        self.db.flush()
        return answer

    def evaluations_for_session(self, session_id: str) -> list[AnswerEvaluation]:
        return [item.evaluation for item in self.results_for_session(session_id)]

    def results_for_session(self, session_id: str) -> list[ReportResult]:
        rows = self.db.execute(
            select(Answer, Question)
            .join(Question, Answer.question_id == Question.id)
            .where(Question.session_id == session_id)
            .order_by(Question.order_index)
        )
        results = []
        for answer, question in rows:
            feedback = json.loads(answer.feedback_json)
            results.append(
                ReportResult(
                    question=ReportQuestion(
                        id=question.id,
                        question_text=question.question_text,
                        question_type=question.question_type,
                        difficulty=question.difficulty,
                        focus_points=json.loads(question.focus_points),
                        reference_direction=question.reference_direction,
                        order_index=question.order_index,
                    ),
                    answer_text=answer.answer_text,
                    evaluation=AnswerEvaluation(
                        score=ScoreBreakdown.model_validate(
                            json.loads(answer.score_json)
                        ),
                        **feedback,
                    ),
                )
            )
        return results


class ReportRepository:
    def __init__(self, db: Session) -> None:
        self.db = db

    def get_for_session(self, session_id: str) -> Report | None:
        return self.db.scalar(select(Report).where(Report.session_id == session_id))

    def create(self, session_id: str, result: InterviewReport) -> Report:
        report = Report(
            session_id=session_id,
            total_score=result.total_score,
            summary=result.summary,
            weaknesses_json=json.dumps(result.weaknesses, ensure_ascii=False),
            recommendations_json=json.dumps(
                result.recommendations, ensure_ascii=False
            ),
        )
        self.db.add(report)
        self.db.flush()
        return report

    @staticmethod
    def to_schema(report: Report) -> InterviewReport:
        return InterviewReport(
            total_score=report.total_score,
            summary=report.summary,
            weaknesses=json.loads(report.weaknesses_json),
            recommendations=json.loads(report.recommendations_json),
        )


class OperationClaimRepository:
    def __init__(self, db: Session) -> None:
        self.db = db

    def acquire(self, resource_key: str, operation_type: str) -> None:
        self.db.add(
            OperationClaim(
                resource_key=resource_key,
                operation_type=operation_type,
            )
        )
        self.db.flush()

    def release(self, resource_key: str) -> None:
        self.db.execute(
            delete(OperationClaim).where(
                OperationClaim.resource_key == resource_key
            )
        )
