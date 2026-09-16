import asyncio
from concurrent.futures import ThreadPoolExecutor
from threading import Event, Lock

import pytest
from fastapi.testclient import TestClient
from sqlalchemy import select
from sqlalchemy.exc import IntegrityError, SQLAlchemyError

from app.main import create_app
from app.models import Answer, OperationClaim, Question, Report
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


def _answer_question(client: TestClient, question_id: str, text="具体回答") -> dict:
    response = client.post(
        f"/api/questions/{question_id}/answers",
        json={"answer_text": text},
    )
    assert response.status_code == 201
    return response.json()


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
    assert report.json()["results"][0]["question"]["id"] == questions[0]["id"]
    assert report.json()["results"][0]["answer_text"] == "我会使用 FastAPI 编写接口并进行参数校验"
    assert report.json()["results"][0]["evaluation"]["score"] == answered.json()["score"]


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


def _assert_no_claim(application, resource_key):
    with application.state.session_factory() as db:
        assert db.get(OperationClaim, resource_key) is None


def test_ai_failure_marks_generation_failed_and_releases_guard(tmp_path):
    application = _application(tmp_path, FailingGenerationProvider())
    client = TestClient(application)
    session_id = _create_interview(client)["id"]

    response = client.post(f"/api/interviews/{session_id}/questions")
    assert response.status_code == 502
    assert response.json() == {"detail": "synthetic AI failure"}
    assert client.get(f"/api/interviews/{session_id}").json()["status"] == "failed"
    assert client.post(f"/api/interviews/{session_id}/questions").status_code == 409
    _assert_no_claim(application, f"questions:{session_id}")


class FailingAnswerProvider(FakeLLMProvider):
    async def evaluate_answer(self, question, answer, context):
        raise AIServiceError("synthetic answer failure")


def test_ai_failure_marks_answer_session_failed_and_blocks_retry(tmp_path):
    application = _application(tmp_path, FailingAnswerProvider())
    client = TestClient(application)
    session_id = _create_interview(client)["id"]
    question_id = _generate_questions(client, session_id)[0]["id"]

    response = client.post(
        f"/api/questions/{question_id}/answers", json={"answer_text": "回答"}
    )

    assert response.status_code == 502
    assert client.get(f"/api/interviews/{session_id}").json()["status"] == "failed"
    assert client.post(
        f"/api/questions/{question_id}/answers", json={"answer_text": "重试"}
    ).status_code == 409
    _assert_no_claim(application, f"answer:{question_id}")


class FailingReportProvider(FakeLLMProvider):
    async def build_report(self, evaluations):
        raise AIServiceError("synthetic report failure")


def test_ai_failure_marks_report_session_failed_and_blocks_retry(tmp_path):
    application = _application(tmp_path, FailingReportProvider())
    client = TestClient(application)
    session_id = _create_interview(client)["id"]
    question_id = _generate_questions(client, session_id)[0]["id"]
    _answer_question(client, question_id)

    response = client.get(f"/api/interviews/{session_id}/report")

    assert response.status_code == 502
    assert client.get(f"/api/interviews/{session_id}").json()["status"] == "failed"
    assert client.get(f"/api/interviews/{session_id}/report").status_code == 409
    _assert_no_claim(application, f"report:{session_id}")


class FailingCreationProvider(FakeLLMProvider):
    async def analyze_job(self, job_description):
        raise AIServiceError("synthetic creation failure")


def test_creation_ai_failure_returns_bad_gateway_without_creating_session(tmp_path):
    client = TestClient(_application(tmp_path, FailingCreationProvider()))

    response = client.post(
        "/api/interviews",
        data={"job_description": "招聘 Python 后端开发"},
        files={"resume": RESUME},
    )

    assert response.status_code == 502
    assert client.get("/api/interviews").json() == {"interviews": []}


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


class CountingReportProvider(FakeLLMProvider):
    def __init__(self):
        self.report_calls = 0
        self.fail_reports = False

    async def build_report(self, evaluations):
        self.report_calls += 1
        if self.fail_reports:
            raise AIServiceError("report provider unavailable")
        return await super().build_report(evaluations)


def test_report_is_saved_once_and_historical_reads_do_not_call_model(tmp_path):
    provider = CountingReportProvider()
    application = _application(tmp_path, provider)
    client = TestClient(application)
    session_id = _create_interview(client)["id"]
    question_id = _generate_questions(client, session_id)[0]["id"]
    _answer_question(client, question_id, "持久化回答")

    first = client.get(f"/api/interviews/{session_id}/report")
    provider.fail_reports = True
    second = client.get(f"/api/interviews/{session_id}/report")

    assert first.status_code == second.status_code == 200
    assert first.json() == second.json()
    assert provider.report_calls == 1
    assert first.json()["results"][0]["answer_text"] == "持久化回答"
    with application.state.session_factory() as db:
        assert len(list(db.scalars(select(Report).where(Report.session_id == session_id)))) == 1


class WrongTotalReportProvider(FakeLLMProvider):
    async def build_report(self, evaluations):
        report = await super().build_report(evaluations)
        return report.model_copy(update={"total_score": 1})


def test_report_total_is_calculated_from_validated_answer_scores(tmp_path):
    application = _application(tmp_path, WrongTotalReportProvider())
    client = TestClient(application)
    session_id = _create_interview(client)["id"]
    question_id = _generate_questions(client, session_id)[0]["id"]
    answer = _answer_question(client, question_id)

    report = client.get(f"/api/interviews/{session_id}/report")

    assert report.status_code == 200
    assert report.json()["total_score"] == answer["score"]["total_score"]
    with application.state.session_factory() as db:
        assert db.scalar(select(Report).where(Report.session_id == session_id)).total_score == answer["score"]["total_score"]


class BlockingReportProvider(FakeLLMProvider):
    def __init__(self):
        self.entered = Event()
        self.release = Event()
        self.calls = 0

    async def build_report(self, evaluations):
        self.calls += 1
        self.entered.set()
        while not self.release.is_set():
            await asyncio.sleep(0.01)
        return await super().build_report(evaluations)


def test_concurrent_report_generation_has_one_writer_and_stable_followup(tmp_path):
    provider = BlockingReportProvider()
    application = _application(tmp_path, provider)
    client = TestClient(application)
    session_id = _create_interview(client)["id"]
    question_id = _generate_questions(client, session_id)[0]["id"]
    _answer_question(client, question_id)

    def report():
        return TestClient(application, raise_server_exceptions=False).get(
            f"/api/interviews/{session_id}/report"
        )

    with ThreadPoolExecutor(max_workers=2) as executor:
        first = executor.submit(report)
        try:
            assert provider.entered.wait(timeout=2)
            second = executor.submit(report)
            second_response = second.result(timeout=10)
        finally:
            provider.release.set()
        first_response = first.result(timeout=10)

    assert sorted([first_response.status_code, second_response.status_code]) == [200, 409]
    stable = client.get(f"/api/interviews/{session_id}/report")
    assert stable.status_code == 200
    assert stable.json() == first_response.json()
    assert provider.calls == 1
    _assert_no_claim(application, f"report:{session_id}")


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


def test_database_constraint_rejects_duplicate_report_session(tmp_path):
    application = _application(tmp_path)
    client = TestClient(application)
    session_id = _create_interview(client)["id"]

    with application.state.session_factory() as db:
        db.add_all(
            [
                Report(
                    session_id=session_id,
                    total_score=7,
                    summary="first",
                    weaknesses_json="[]",
                    recommendations_json="[]",
                ),
                Report(
                    session_id=session_id,
                    total_score=8,
                    summary="second",
                    weaknesses_json="[]",
                    recommendations_json="[]",
                ),
            ]
        )
        with pytest.raises(IntegrityError):
            db.commit()


def test_temporary_sqlite_database_is_not_left_open(tmp_path):
    database_path = tmp_path / "disposable.db"
    application = _application(tmp_path, database_name="disposable.db")

    assert TestClient(application).get("/api/health").status_code == 200
    database_path.unlink()

    assert not database_path.exists()
