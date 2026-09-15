from pathlib import Path

from fastapi import FastAPI, HTTPException, Request
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse

from app.api.interviews import router as interviews_router
from app.api.questions import router as questions_router
from app.config import Settings
from app.db import create_engine_and_session, init_db
from app.services.interview_service import (
    DatabaseServiceError,
    InterviewConflictError,
    InterviewNotFoundError,
    InterviewService,
    InvalidInterviewInputError,
)
from app.services.llm import AIServiceError, LLMProvider, OpenAICompatibleLLM
from app.services.retriever import Retriever, TfidfRetriever


BACKEND_DATA_DIR = Path(__file__).resolve().parents[1] / "data"
ALLOWED_ORIGINS = (
    "http://localhost:5173",
    "http://127.0.0.1:5173",
)


def create_app(
    database_url: str | None = None,
    llm_provider: LLMProvider | None = None,
    retriever: Retriever | None = None,
) -> FastAPI:
    BACKEND_DATA_DIR.mkdir(parents=True, exist_ok=True)
    application = FastAPI(title="AI Interview Platform")
    application.add_middleware(
        CORSMiddleware,
        allow_origins=list(ALLOWED_ORIGINS),
        allow_credentials=False,
        allow_methods=["*"],
        allow_headers=["*"],
    )
    settings = Settings()
    resolved_database_url = database_url or settings.database_url
    if resolved_database_url.startswith("sqlite:///./"):
        Path(resolved_database_url.removeprefix("sqlite:///./")).parent.mkdir(
            parents=True, exist_ok=True
        )
    engine, session_factory = create_engine_and_session(resolved_database_url)
    init_db(engine)

    application.state.settings = settings
    application.state.database_url = resolved_database_url
    application.state.session_factory = session_factory
    application.state.llm_provider = llm_provider or OpenAICompatibleLLM.from_settings(
        settings
    )
    application.state.retriever = retriever or TfidfRetriever()
    application.state.interview_service = InterviewService(
        session_factory,
        application.state.llm_provider,
        application.state.retriever,
    )

    @application.get("/api/health")
    def health() -> dict[str, str]:
        return {"status": "ok", "service": "ai-interview-platform"}

    @application.exception_handler(InvalidInterviewInputError)
    async def invalid_input_handler(
        request: Request, exc: InvalidInterviewInputError
    ):
        raise HTTPException(status_code=400, detail=str(exc))

    @application.exception_handler(InterviewNotFoundError)
    async def not_found_handler(request: Request, exc: InterviewNotFoundError):
        raise HTTPException(status_code=404, detail=str(exc))

    @application.exception_handler(InterviewConflictError)
    async def conflict_handler(request: Request, exc: InterviewConflictError):
        raise HTTPException(status_code=409, detail=str(exc))

    @application.exception_handler(AIServiceError)
    async def ai_service_handler(request: Request, exc: AIServiceError):
        raise HTTPException(status_code=502, detail=str(exc))

    @application.exception_handler(DatabaseServiceError)
    async def database_service_handler(
        request: Request, exc: DatabaseServiceError
    ) -> JSONResponse:
        return JSONResponse(
            status_code=503,
            content={"detail": "database service unavailable"},
        )

    application.include_router(interviews_router)
    application.include_router(questions_router)

    return application


app = create_app()
