from fastapi import FastAPI


def create_app() -> FastAPI:
    application = FastAPI(title="AI Interview Platform")

    @application.get("/api/health")
    def health() -> dict[str, str]:
        return {"status": "ok", "service": "ai-interview-platform"}

    return application


app = create_app()
