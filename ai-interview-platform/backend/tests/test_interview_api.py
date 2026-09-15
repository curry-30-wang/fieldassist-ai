from fastapi.testclient import TestClient

from app.main import create_app
from app.services.llm import FakeLLMProvider


def test_interview_api_completes_the_core_flow(tmp_path):
    application = create_app(
        database_url=f"sqlite:///{tmp_path / 'test.db'}",
        llm_provider=FakeLLMProvider(),
    )
    client = TestClient(application)

    created = client.post(
        "/api/interviews",
        data={"job_description": "招聘 Python FastAPI 后端开发"},
        files={"resume": ("resume.txt", b"\xe6\x88\x91\xe5\x81\x9a\xe8\xbf\x87 FastAPI \xe9\xa1\xb9\xe7\x9b\xae", "text/plain")},
    )
    assert created.status_code == 201
    session_id = created.json()["id"]

    generated = client.post(f"/api/interviews/{session_id}/questions")
    assert generated.status_code == 200
    assert len(generated.json()["questions"]) == 5
    question_id = generated.json()["questions"][0]["id"]

    answered = client.post(
        f"/api/questions/{question_id}/answers",
        json={"answer_text": "我会使用 FastAPI 编写接口并进行参数校验"},
    )
    assert answered.status_code == 201
    assert 0 <= answered.json()["score"]["total_score"] <= 10

    report = client.get(f"/api/interviews/{session_id}/report")
    assert report.status_code == 200
    assert "summary" in report.json()


def test_temporary_sqlite_database_is_not_left_open(tmp_path):
    database_path = tmp_path / "disposable.db"
    application = create_app(
        database_url=f"sqlite:///{database_path}",
        llm_provider=FakeLLMProvider(),
    )

    assert TestClient(application).get("/api/health").status_code == 200
    database_path.unlink()

    assert not database_path.exists()
