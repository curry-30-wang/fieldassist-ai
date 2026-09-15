import json
from collections.abc import Callable

from sqlalchemy.exc import SQLAlchemyError
from sqlalchemy.orm import Session

from app.domain import InterviewStatus, can_transition
from app.models import InterviewSession, Question
from app.repositories import (
    AnswerRepository,
    DocumentRepository,
    InterviewRepository,
    QuestionRepository,
    ReportRepository,
)
from app.schemas import AnswerEvaluation, GeneratedQuestion, InterviewReport
from app.services.document_parser import DocumentParser, split_text
from app.services.llm import AIServiceError, LLMProvider
from app.services.retriever import ChunkRecord, Retriever


class InterviewNotFoundError(LookupError):
    pass


class InterviewConflictError(RuntimeError):
    pass


class InvalidInterviewInputError(ValueError):
    pass


class InterviewService:
    def __init__(
        self,
        session_factory: Callable[[], Session],
        llm_provider: LLMProvider,
        retriever: Retriever,
        document_parser: DocumentParser | None = None,
    ) -> None:
        self.session_factory = session_factory
        self.llm_provider = llm_provider
        self.retriever = retriever
        self.document_parser = document_parser or DocumentParser()

    async def create_session(
        self,
        job_description: str,
        filename: str,
        content_type: str,
        data: bytes,
    ) -> InterviewSession:
        job_description = job_description.strip()
        if not job_description:
            raise InvalidInterviewInputError("job description must not be empty")
        try:
            text = self.document_parser.parse(filename, content_type, data)
        except Exception as exc:
            raise InvalidInterviewInputError("invalid resume document") from exc

        analysis = await self.llm_provider.analyze_job(job_description)
        chunks = split_text(text)
        with self.session_factory() as db:
            try:
                interviews = InterviewRepository(db)
                documents = DocumentRepository(db)
                interview = interviews.create(analysis.job_title, job_description)
                document = documents.create(filename, content_type, text, chunks)
                interview.resume_document_id = document.id
                db.commit()
            except SQLAlchemyError:
                db.rollback()
                raise

        self.retriever.index(
            [ChunkRecord(document.id, index, chunk) for index, chunk in enumerate(chunks)]
        )
        return interview

    async def generate_questions(self, session_id: str) -> list[Question]:
        with self.session_factory() as db:
            interviews = InterviewRepository(db)
            questions = QuestionRepository(db)
            interview = interviews.get(session_id)
            if interview is None:
                raise InterviewNotFoundError("interview session not found")
            if interview.status != InterviewStatus.CREATED.value:
                raise InterviewConflictError("questions can only be generated once")

            context = self._search_context(db, interview)
            analysis = await self.llm_provider.analyze_job(interview.job_description)
            question_set = await self.llm_provider.generate_questions(analysis, context)
            if len(question_set.questions) != 5:
                raise AIServiceError("AI service must return exactly five questions")

            try:
                stored = questions.create_many(session_id, question_set.questions)
                self._transition(interview, InterviewStatus.IN_PROGRESS)
                db.commit()
                return stored
            except SQLAlchemyError:
                db.rollback()
                raise

    async def submit_answer(
        self, question_id: str, answer_text: str
    ) -> AnswerEvaluation:
        answer_text = answer_text.strip()
        if not answer_text:
            raise InvalidInterviewInputError("answer text must not be empty")

        with self.session_factory() as db:
            interviews = InterviewRepository(db)
            questions = QuestionRepository(db)
            answers = AnswerRepository(db)
            question = questions.get(question_id)
            if question is None:
                raise InterviewNotFoundError("question not found")
            interview = interviews.get(question.session_id)
            if interview is None:
                raise InterviewNotFoundError("interview session not found")
            if interview.status != InterviewStatus.IN_PROGRESS.value:
                raise InterviewConflictError("interview session does not accept answers")
            if answers.get_for_question(question_id) is not None:
                raise InterviewConflictError("question has already been answered")

            context = self._search_context(db, interview, question.question_text)
            generated = GeneratedQuestion(
                question_text=question.question_text,
                question_type=question.question_type,
                difficulty=question.difficulty,
                focus_points=json.loads(question.focus_points),
                reference_direction=question.reference_direction,
            )
            evaluation = await self.llm_provider.evaluate_answer(
                generated, answer_text, context
            )
            try:
                answers.create(question_id, answer_text, evaluation)
                db.commit()
                return evaluation
            except SQLAlchemyError:
                db.rollback()
                raise

    async def get_report(self, session_id: str) -> InterviewReport:
        with self.session_factory() as db:
            interviews = InterviewRepository(db)
            questions = QuestionRepository(db)
            answers = AnswerRepository(db)
            reports = ReportRepository(db)
            interview = interviews.get(session_id)
            if interview is None:
                raise InterviewNotFoundError("interview session not found")

            evaluations = answers.evaluations_for_session(session_id)
            result = await self.llm_provider.build_report(evaluations)
            try:
                reports.upsert(session_id, result)
                question_count = len(questions.list_for_session(session_id))
                if (
                    interview.status == InterviewStatus.IN_PROGRESS.value
                    and question_count > 0
                    and len(evaluations) == question_count
                ):
                    self._transition(interview, InterviewStatus.COMPLETED)
                db.commit()
                return result
            except SQLAlchemyError:
                db.rollback()
                raise

    def list_sessions(self) -> list[InterviewSession]:
        with self.session_factory() as db:
            return InterviewRepository(db).list()

    def get_session(self, session_id: str) -> tuple[InterviewSession, list[Question]]:
        with self.session_factory() as db:
            interview = InterviewRepository(db).get(session_id)
            if interview is None:
                raise InterviewNotFoundError("interview session not found")
            questions = QuestionRepository(db).list_for_session(session_id)
            return interview, questions

    def _search_context(
        self,
        db: Session,
        interview: InterviewSession,
        query: str | None = None,
    ):
        chunks = DocumentRepository(db).chunks(interview.resume_document_id)
        self.retriever.index(
            [
                ChunkRecord(chunk.document_id, chunk.chunk_index, chunk.content)
                for chunk in chunks
            ]
        )
        return self.retriever.search(
            query or f"{interview.job_title} {interview.job_description}"
        )

    @staticmethod
    def _transition(interview: InterviewSession, target: InterviewStatus) -> None:
        current = InterviewStatus(interview.status)
        if not can_transition(current, target):
            raise InterviewConflictError(
                f"cannot transition interview from {current.value} to {target.value}"
            )
        interview.status = target.value
