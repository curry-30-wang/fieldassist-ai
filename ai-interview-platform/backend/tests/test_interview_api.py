import asyncio
from concurrent.futures import ThreadPoolExecutor
from threading import Event, Lock

import pytest
from fastapi.testclient import TestClient
from sqlalchemy.exc import IntegrityError, SQLAlchemyError

from app.main import create_app
from app.models import Answer, Question
from app.repositories import QuestionRepository
from app.services.llm import AIServiceError, FakeLLMProvider


RESUME = ("resume.txt", "我做过 FastAPI 项目".encode(), "text/plain")


def _application(tmp_path, provider=None, database_name="test.db"):
    return create_app(
        database_url=f"sqlite:///{tmp_path / database_name}",
        llm_provider=provider or FakeLLMProvider(),
    )


def _create_interview(client: TestClient) -> dict:
    response = client.post(
        "/api/interviews",
        data={"job_description": "招聘 Python FastAPI 后端开发"},
        files={"resume": RESUME},
    )
    assert response.status_code == 201
    return response.json()


def _generate_questions(client: TestClient, session_id: str) -> list[dict]:
    response = client.post(f"/api/interviews/{session_id}/questions")
    assert response.status_code == 200
    questions = response.json()["questions"]
    assert len(questions) == 5
    return questions


def test_interview_api_completes_the_core_flow(tmp_path):
    client = TestClient(_application(tmp_path))
    session_id = _create_interview(client)["id"]
    questions = _generate_questions(client, session_id)

    answered = client.post(
        f"/api/questions/{questions[0]['id']}/answers",
        json={"answer_text": "我会使用 FastAPI 编写接口并进行参数校验"},
    )
    assert answered.status_code == 201
    assert 0 <= answered.json()["score"]["total_score"] <= 10

    report = client.get(f"/api/interviews/{session_id}/report")
    assert report.status_code == 200
    assert "summary" in report.json()


def test_list_and_detail_routes_return_created_session_and_questions(tmp_path):
    client = TestClient(_application(tmp_path))
    created = _create_interview(client)

    listed = client.get("/api/interviews")
    assert listed.status_code == 200
    assert [item["id"] for item in listed.json()["interviews"]] == [created["id"]]

    before_generation = client.get(f"/api/interviews/{created['id']}")
    assert before_generation.status_code == 200
    assert before_generation.json()["status"] == "created"
    assert before_generation.json()["questions"] == []

    generated = _generate_questions(client, created["id"])
    detail = client.get(f"/api/interviews/{created['id']}")
    assert detail.status_code == 200
    assert detail.json()["status"] == "in_progress"
    assert [item["id"] for item in detail.json()["questions"]] == [
        item["id"] for item in generated
    ]


def test_all_five_answers_complete_the_interview_and_expose_four_dimensions(tmp_path):
    client = TestClient(_application(tmp_path))
    session_id = _create_interview(client)["id"]
    questions = _generate_questions(client, session_id)

    for question in questions:
        response = client.post(
            f"/api/questions/{question['id']}/answers",
            json={"answer_text": "用具体项目、验证步骤和结果回答问题"},
        )
        assert response.status_code == 201
        assert set(response.json()["score"]) == {
            "accuracy",
            "completeness",
            "relevance",
            "clarity",
            "total_score",
        }
        assert all(0 <= value <= 10 for value in response.json()["score"].values())

    report = client.get(f"/api/interviews/{session_id}/report")
    assert report.status_code == 200
    assert client.get(f"/api/interviews/{session_id}").json()["status"] == "completed"

    completed_answer = client.post(
        f"/api/questions/{questions[-1]['id']}/answers",
        json={"answer_text": "完成后不能继续提交"},
    )
    assert completed_answer.status_code == 409


def test_duplicate_generation_and_answer_return_conflict(tmp_path):
    client = TestClient(_application(tmp_path))
    session_id = _create_interview(client)["id"]
    questions = _generate_questions(client, session_id)

    duplicate_generation = client.post(f"/api/interviews/{session_id}/questions")
    assert duplicate_generation.status_code == 409

    first_answer = client.post(
        f"/api/questions/{questions[0]['id']}/answers",
        json={"answer_text": "首次回答"},
    )
    duplicate_answer = client.post(
        f"/api/questions/{questions[0]['id']}/answers",
        json={"answer_text": "重复回答"},
    )
    assert first_answer.status_code == 201
    assert duplicate_answer.status_code == 409


def test_invalid_file_and_missing_resources_have_stable_classification(tmp_path):
    client = TestClient(_application(tmp_path))

    invalid_file = client.post(
        "/api/interviews",
        data={"job_description": "招聘后端开发"},
        files={"resume": ("resume.exe", b"not a resume", "application/octet-stream")},
    )
    assert invalid_file.status_code == 400
    assert invalid_file.json() == {"detail": "invalid resume document"}

    assert client.get("/api/interviews/missing").status_code == 404
    assert client.post("/api/interviews/missing/questions").status_code == 404
    assert client.get("/api/interviews/missing/report").status_code == 404
    assert (
        client.post(
            "/api/questions/missing/answers", json={"answer_text": "回答"}
        ).status_code
        == 404
    )


class FailingGenerationProvider(FakeLLMProvider):
    async def generate_questions(self, job, context):
        raise AIServiceError("synthetic AI failure")


def test_ai_failure_returns_bad_gateway_and_releases_generation_guard(tmp_path):
    client = TestClient(_application(tmp_path, FailingGenerationProvider()))
    session_id = _create_interview(client)["id"]

    response = client.post(f"/api/interviews/{session_id}/questions")
    assert response.status_code == 502
    assert response.json() == {"detail": "synthetic AI failure"}
    assert client.get(f"/api/interviews/{session_id}").json()["status"] == "created"


def test_database_failure_returns_safe_json_and_rolls_back(monkeypatch, tmp_path):
    application = _application(tmp_path)
    client = TestClient(application, raise_server_exceptions=False)
    session_id = _create_interview(client)["id"]
    original_create_many = QuestionRepository.create_many

    def fail_after_flush(repository, requested_session_id, generated):
        original_create_many(repository, requested_session_id, generated)
        raise SQLAlchemyError("secret database coordinates")

    monkeypatch.setattr(QuestionRepository, "create_many", fail_after_flush)

    response = client.post(f"/api/interviews/{session_id}/questions")
    assert response.status_code == 503
    assert response.json() == {"detail": "database service unavailable"}
    assert "secret" not in response.text

    detail = client.get(f"/api/interviews/{session_id}").json()
    assert detail["status"] == "created"
    assert detail["questions"] == []


class BlockingGenerationProvider(FakeLLMProvider):
    def __init__(self):
        self.entered = Event()
        self.release = Event()
        self.calls = 0
        self.lock = Lock()

    async def generate_questions(self, job, context):
        with self.lock:
            self.calls += 1
            call_number = self.calls
        if call_number == 1:
            self.entered.set()
            while not self.release.is_set():
                await asyncio.sleep(0.01)
        return await super().generate_questions(job, context)


def test_concurrent_generation_returns_one_success_and_one_conflict(tmp_path):
    provider = BlockingGenerationProvider()
    application = _application(tmp_path, provider)
    session_id = _create_interview(TestClient(application))["id"]

    def generate():
        return TestClient(application, raise_server_exceptions=False).post(
            f"/api/interviews/{session_id}/questions"
        )

    with ThreadPoolExecutor(max_workers=2) as executor:
        first = executor.submit(generate)
        try:
            assert provider.entered.wait(timeout=2)
            second = executor.submit(generate)
            second_response = second.result(timeout=10)
        finally:
            provider.release.set()
        responses = [first.result(timeout=10), second_response]

    assert sorted(response.status_code for response in responses) == [200, 409]
    assert provider.calls == 1
    detail = TestClient(application).get(f"/api/interviews/{session_id}").json()
    assert len(detail["questions"]) == 5


class BlockingAnswerProvider(FakeLLMProvider):
    def __init__(self):
        self.entered = Event()
        self.release = Event()
        self.calls = 0
        self.lock = Lock()

    async def evaluate_answer(self, question, answer, context):
        with self.lock:
            self.calls += 1
            call_number = self.calls
        if call_number == 1:
            self.entered.set()
            while not self.release.is_set():
                await asyncio.sleep(0.01)
        return await super().evaluate_answer(question, answer, context)


def test_concurrent_answers_return_one_success_and_one_conflict(tmp_path):
    provider = BlockingAnswerProvider()
    application = _application(tmp_path, provider)
    client = TestClient(application)
    session_id = _create_interview(client)["id"]
    question_id = _generate_questions(client, session_id)[0]["id"]

    def answer(text):
        return TestClient(application, raise_server_exceptions=False).post(
            f"/api/questions/{question_id}/answers", json={"answer_text": text}
        )

    with ThreadPoolExecutor(max_workers=2) as executor:
        first = executor.submit(answer, "首次并发回答")
        try:
            assert provider.entered.wait(timeout=2)
            second = executor.submit(answer, "重复并发回答")
            second_response = second.result(timeout=10)
        finally:
            provider.release.set()
        responses = [first.result(timeout=10), second_response]

    assert sorted(response.status_code for response in responses) == [201, 409]
    assert provider.calls == 1


def test_database_constraints_reject_duplicate_question_order_and_answer(tmp_path):
    application = _application(tmp_path)
    client = TestClient(application)
    session_id = _create_interview(client)["id"]
    questions = _generate_questions(client, session_id)
    question_id = questions[0]["id"]

    with application.state.session_factory() as db:
        original = db.get(Question, question_id)
        db.add(
            Question(
                session_id=session_id,
                question_text="重复顺序",
                question_type=original.question_type,
                difficulty=original.difficulty,
                focus_points=original.focus_points,
                reference_direction=original.reference_direction,
                order_index=original.order_index,
            )
        )
        with pytest.raises(IntegrityError):
            db.commit()
        db.rollback()

    assert (
        client.post(
            f"/api/questions/{question_id}/answers", json={"answer_text": "首次回答"}
        ).status_code
        == 201
    )
    with application.state.session_factory() as db:
        db.add(
            Answer(
                question_id=question_id,
                answer_text="数据库重复回答",
                score_json="{}",
                feedback_json="{}",
            )
        )
        with pytest.raises(IntegrityError):
            db.commit()


def test_temporary_sqlite_database_is_not_left_open(tmp_path):
    database_path = tmp_path / "disposable.db"
    application = _application(tmp_path, database_name="disposable.db")

    assert TestClient(application).get("/api/health").status_code == 200
    database_path.unlink()

    assert not database_path.exists()
