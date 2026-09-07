from pathlib import Path

from fastapi import FastAPI
from fastapi.staticfiles import StaticFiles

from backend.app.config import get_settings


FRONTEND_DIRECTORY = Path(__file__).resolve().parents[2] / "frontend"


def create_app() -> FastAPI:
    settings = get_settings()
    application = FastAPI(title=settings.app_name)

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

    application.mount("/", StaticFiles(directory=FRONTEND_DIRECTORY, html=True), name="frontend")
    return application


app = create_app()
