import json
from collections.abc import Callable

from sqlalchemy.exc import IntegrityError, SQLAlchemyError
from sqlalchemy.orm import Session

from app.domain import InterviewStatus, can_transition
from app.models import InterviewSession, Question
from app.repositories import (
    AnswerRepository,
    DocumentRepository,
    InterviewRepository,
    OperationClaimRepository,
    QuestionRepository,
    ReportRepository,
)
from app.schemas import (
    AnswerEvaluation,
    GeneratedQuestion,
    InterviewReport,
    InterviewReportResponse,
    ReportResult,
)
from app.services.document_parser import DocumentParser, split_text
from app.services.llm import AIServiceError, LLMProvider
from app.services.retriever import ChunkRecord, Retriever


class InterviewNotFoundError(LookupError):
    pass


class InterviewConflictError(RuntimeError):
    pass


class InvalidInterviewInputError(ValueError):
    pass


class DatabaseServiceError(RuntimeError):
    """Raised when interview persistence is temporarily unavailable."""


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
            except SQLAlchemyError as exc:
                db.rollback()
                raise DatabaseServiceError("database service unavailable") from exc

        self.retriever.index(
            [ChunkRecord(document.id, index, chunk) for index, chunk in enumerate(chunks)]
        )
        return interview

    async def generate_questions(self, session_id: str) -> list[Question]:
        claim_key = f"questions:{session_id}"
        self._validate_generation_state(session_id)
        self._acquire_claim(claim_key, "generate_questions")

        with self.session_factory() as db:
            try:
                interviews = InterviewRepository(db)
                questions = QuestionRepository(db)
                claims = OperationClaimRepository(db)
                interview = interviews.get(session_id)
                if interview is None:
                    raise InterviewNotFoundError("interview session not found")
                if interview.status != InterviewStatus.CREATED.value:
                    raise InterviewConflictError("questions can only be generated once")

                context = self._search_context(db, interview)
                analysis = await self.llm_provider.analyze_job(
                    interview.job_description
                )
                question_set = await self.llm_provider.generate_questions(
                    analysis, context
                )
                if len(question_set.questions) != 5:
                    raise AIServiceError(
                        "AI service must return exactly five questions"
                    )

                stored = questions.create_many(session_id, question_set.questions)
                self._transition(interview, InterviewStatus.IN_PROGRESS)
                claims.release(claim_key)
                db.commit()
                return stored
            except AIServiceError:
                db.rollback()
                self._fail_session_and_release_claim(session_id, claim_key)
                raise
            except (InterviewNotFoundError, InterviewConflictError):
                db.rollback()
                self._release_claim(claim_key)
                raise
            except IntegrityError as exc:
                db.rollback()
                self._release_claim(claim_key)
                raise InterviewConflictError(
                    "questions have already been generated"
                ) from exc
            except SQLAlchemyError as exc:
                db.rollback()
                self._release_claim(claim_key)
                raise DatabaseServiceError("database service unavailable") from exc

    async def submit_answer(
        self, question_id: str, answer_text: str
    ) -> AnswerEvaluation:
        answer_text = answer_text.strip()
        if not answer_text:
            raise InvalidInterviewInputError("answer text must not be empty")

        session_id = self._validate_answer_state(question_id)
        claim_key = f"answer:{question_id}"
        self._acquire_claim(claim_key, "submit_answer")

        with self.session_factory() as db:
            try:
                interviews = InterviewRepository(db)
                questions = QuestionRepository(db)
                answers = AnswerRepository(db)
                claims = OperationClaimRepository(db)
                question = questions.get(question_id)
                if question is None:
                    raise InterviewNotFoundError("question not found")
                interview = interviews.get(question.session_id)
                if interview is None:
                    raise InterviewNotFoundError("interview session not found")
                if interview.status != InterviewStatus.IN_PROGRESS.value:
                    raise InterviewConflictError(
                        "interview session does not accept answers"
                    )
                if answers.get_for_question(question_id) is not None:
                    raise InterviewConflictError(
                        "question has already been answered"
                    )

                context = self._search_context(
                    db, interview, question.question_text
                )
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
                answers.create(question_id, answer_text, evaluation)
                claims.release(claim_key)
                db.commit()
                return evaluation
            except AIServiceError:
                db.rollback()
                self._fail_session_and_release_claim(session_id, claim_key)
                raise
            except (InterviewNotFoundError, InterviewConflictError):
                db.rollback()
                self._release_claim(claim_key)
                raise
            except IntegrityError as exc:
                db.rollback()
                self._release_claim(claim_key)
                raise InterviewConflictError(
                    "question has already been answered"
                ) from exc
            except SQLAlchemyError as exc:
                db.rollback()
                self._release_claim(claim_key)
                raise DatabaseServiceError("database service unavailable") from exc

    async def get_report(self, session_id: str) -> InterviewReportResponse:
        saved = self._saved_report(session_id)
        if saved is not None:
            return saved

        self._validate_report_state(session_id)
        claim_key = f"report:{session_id}"
        self._acquire_claim(claim_key, "build_report")

        with self.session_factory() as db:
            try:
                interviews = InterviewRepository(db)
                questions = QuestionRepository(db)
                answers = AnswerRepository(db)
                reports = ReportRepository(db)
                claims = OperationClaimRepository(db)
                interview = interviews.get(session_id)
                if interview is None:
                    raise InterviewNotFoundError("interview session not found")
                saved_report = reports.get_for_session(session_id)
                if saved_report is not None:
                    claims.release(claim_key)
                    response = self._report_response(
                        reports.to_schema(saved_report),
                        answers.results_for_session(session_id),
                    )
                    db.commit()
                    return response
                if interview.status != InterviewStatus.IN_PROGRESS.value:
                    raise InterviewConflictError("interview report is not available")

                results = answers.results_for_session(session_id)
                if not results:
                    raise InterviewConflictError(
                        "interview report requires at least one answer"
                    )
                evaluations = [item.evaluation for item in results]
                generated = await self.llm_provider.build_report(evaluations)
                total_score = round(
                    sum(item.score.total_score for item in evaluations)
                    / len(evaluations),
                    2,
                )
                result = InterviewReport.model_validate(
                    {**generated.model_dump(), "total_score": total_score}
                )
                reports.create(session_id, result)
                question_count = len(questions.list_for_session(session_id))
                if (
                    interview.status == InterviewStatus.IN_PROGRESS.value
                    and question_count > 0
                    and len(evaluations) == question_count
                ):
                    self._transition(interview, InterviewStatus.COMPLETED)
                claims.release(claim_key)
                db.commit()
                return self._report_response(result, results)
            except AIServiceError:
                db.rollback()
                self._fail_session_and_release_claim(session_id, claim_key)
                raise
            except (InterviewNotFoundError, InterviewConflictError):
                db.rollback()
                self._release_claim(claim_key)
                raise
            except IntegrityError as exc:
                db.rollback()
                self._release_claim(claim_key)
                saved = self._saved_report(session_id)
                if saved is not None:
                    return saved
                raise InterviewConflictError(
                    "interview report is already being generated"
                ) from exc
            except SQLAlchemyError as exc:
                db.rollback()
                self._release_claim(claim_key)
                raise DatabaseServiceError("database service unavailable") from exc

    def list_sessions(self) -> list[InterviewSession]:
        with self.session_factory() as db:
            try:
                return InterviewRepository(db).list()
            except SQLAlchemyError as exc:
                db.rollback()
                raise DatabaseServiceError("database service unavailable") from exc

    def get_session(self, session_id: str) -> tuple[InterviewSession, list[Question]]:
        with self.session_factory() as db:
            try:
                interview = InterviewRepository(db).get(session_id)
                if interview is None:
                    raise InterviewNotFoundError("interview session not found")
                questions = QuestionRepository(db).list_for_session(session_id)
                return interview, questions
            except SQLAlchemyError as exc:
                db.rollback()
                raise DatabaseServiceError("database service unavailable") from exc

    def _validate_generation_state(self, session_id: str) -> None:
        with self.session_factory() as db:
            try:
                interview = InterviewRepository(db).get(session_id)
                if interview is None:
                    raise InterviewNotFoundError("interview session not found")
                if interview.status != InterviewStatus.CREATED.value:
                    raise InterviewConflictError(
                        "questions can only be generated once"
                    )
            except SQLAlchemyError as exc:
                db.rollback()
                raise DatabaseServiceError("database service unavailable") from exc

    def _validate_answer_state(self, question_id: str) -> str:
        with self.session_factory() as db:
            try:
                question = QuestionRepository(db).get(question_id)
                if question is None:
                    raise InterviewNotFoundError("question not found")
                interview = InterviewRepository(db).get(question.session_id)
                if interview is None:
                    raise InterviewNotFoundError("interview session not found")
                if interview.status != InterviewStatus.IN_PROGRESS.value:
                    raise InterviewConflictError(
                        "interview session does not accept answers"
                    )
                if AnswerRepository(db).get_for_question(question_id) is not None:
                    raise InterviewConflictError(
                        "question has already been answered"
                    )
                return interview.id
            except SQLAlchemyError as exc:
                db.rollback()
                raise DatabaseServiceError("database service unavailable") from exc

    def _validate_report_state(self, session_id: str) -> None:
        with self.session_factory() as db:
            try:
                interview = InterviewRepository(db).get(session_id)
                if interview is None:
                    raise InterviewNotFoundError("interview session not found")
                if interview.status != InterviewStatus.IN_PROGRESS.value:
                    raise InterviewConflictError("interview report is not available")
            except SQLAlchemyError as exc:
                db.rollback()
                raise DatabaseServiceError("database service unavailable") from exc

    def _saved_report(self, session_id: str) -> InterviewReportResponse | None:
        with self.session_factory() as db:
            try:
                interview = InterviewRepository(db).get(session_id)
                if interview is None:
                    raise InterviewNotFoundError("interview session not found")
                reports = ReportRepository(db)
                report = reports.get_for_session(session_id)
                if report is None:
                    return None
                results = AnswerRepository(db).results_for_session(session_id)
                return self._report_response(reports.to_schema(report), results)
            except SQLAlchemyError as exc:
                db.rollback()
                raise DatabaseServiceError("database service unavailable") from exc

    def _acquire_claim(self, resource_key: str, operation_type: str) -> None:
        with self.session_factory() as db:
            try:
                OperationClaimRepository(db).acquire(
                    resource_key, operation_type
                )
                db.commit()
            except IntegrityError as exc:
                db.rollback()
                raise InterviewConflictError(
                    "request is already being processed"
                ) from exc
            except SQLAlchemyError as exc:
                db.rollback()
                raise DatabaseServiceError("database service unavailable") from exc

    def _release_claim(self, resource_key: str) -> None:
        with self.session_factory() as db:
            try:
                OperationClaimRepository(db).release(resource_key)
                db.commit()
            except SQLAlchemyError as exc:
                db.rollback()
                raise DatabaseServiceError("database service unavailable") from exc

    def _fail_session_and_release_claim(
        self, session_id: str, resource_key: str
    ) -> None:
        with self.session_factory() as db:
            try:
                interview = InterviewRepository(db).get(session_id)
                if interview is not None:
                    current = InterviewStatus(interview.status)
                    if can_transition(current, InterviewStatus.FAILED):
                        interview.status = InterviewStatus.FAILED.value
                OperationClaimRepository(db).release(resource_key)
                db.commit()
            except SQLAlchemyError as exc:
                db.rollback()
                raise DatabaseServiceError("database service unavailable") from exc

    @staticmethod
    def _report_response(
        report: InterviewReport, results: list[ReportResult]
    ) -> InterviewReportResponse:
        return InterviewReportResponse(**report.model_dump(), results=results)

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
