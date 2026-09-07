from pathlib import Path

from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse
from fastapi.staticfiles import StaticFiles
from starlette.middleware.sessions import SessionMiddleware

from backend.app.api.auth import router as auth_router
from backend.app.config import get_settings
from backend.app.dependencies import SafeHTTPException


FRONTEND_DIRECTORY = Path(__file__).resolve().parents[2] / "frontend"


def create_app() -> FastAPI:
    settings = get_settings()
    application = FastAPI(title=settings.app_name)
    application.add_middleware(SessionMiddleware, secret_key=settings.secret_key)

    @application.exception_handler(SafeHTTPException)
    async def safe_http_exception_handler(
        request: Request,
        exception: SafeHTTPException,
    ) -> JSONResponse:
        return JSONResponse(
            status_code=exception.status_code,
            content={"success": False, "data": None, "message": exception.detail},
        )

    @application.get("/api/health")
    def health() -> dict[str, object]:
        return {
            "success": True,
            "data": {
                "status": "ok",
                "ai_provider": settings.ai_provider,
                "observability_provider": settings.observability_provider,
            },
            "message": "FieldAssist is healthy",
        }

    application.include_router(auth_router)
    application.mount("/", StaticFiles(directory=FRONTEND_DIRECTORY, html=True), name="frontend")
    return application


app = create_app()
