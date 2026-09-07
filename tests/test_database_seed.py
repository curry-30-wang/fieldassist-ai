import base64
import hashlib
import hmac
import importlib

import pytest
from sqlalchemy import create_engine, inspect, select


EXPECTED_TABLES = {
    "ai_runs",
    "conversations",
    "evaluation_cases",
    "evaluation_results",
    "evaluation_runs",
    "feedback",
    "knowledge_documents",
    "messages",
    "tickets",
    "users",
}

EXPECTED_FOREIGN_KEYS = {
    "conversations": {("user_id", "users")},
    "messages": {("conversation_id", "conversations")},
    "ai_runs": {("message_id", "messages")},
    "feedback": {("message_id", "messages"), ("user_id", "users")},
    "tickets": {("source_message_id", "messages"), ("created_by", "users")},
    "evaluation_results": {
        ("run_id", "evaluation_runs"),
        ("case_id", "evaluation_cases"),
    },
}

EXPECTED_COLUMNS = {
    "users": {"id", "email", "display_name", "password_hash", "role", "is_active", "created_at"},
    "conversations": {"id", "user_id", "title", "dify_conversation_id", "created_at"},
    "messages": {"id", "conversation_id", "role", "content", "provider", "sources_json", "created_at"},
    "ai_runs": {
        "id",
        "message_id",
        "provider",
        "status",
        "trace_id",
        "model",
        "latency_ms",
        "input_tokens",
        "output_tokens",
        "total_cost",
        "error_code",
        "created_at",
    },
    "feedback": {"id", "message_id", "user_id", "rating", "comment", "created_at"},
    "tickets": {
        "id",
        "source_message_id",
        "created_by",
        "title",
        "description",
        "priority",
        "status",
        "created_at",
        "updated_at",
    },
    "knowledge_documents": {"id", "title", "category", "content", "source_url", "enabled", "created_at"},
    "evaluation_cases": {"id", "question", "category", "expected_keywords_json", "created_at"},
    "evaluation_runs": {"id", "provider", "total_cases", "passed_cases", "score", "started_at", "completed_at"},
    "evaluation_results": {"id", "run_id", "case_id", "answer", "matched", "latency_ms", "error_message"},
}


def load_task_modules():
    try:
        models = importlib.import_module("backend.app.models")
        seed = importlib.import_module("backend.seed")
    except ModuleNotFoundError as exc:
        pytest.fail(f"Task 2 module is missing: {exc.name}")
    return models, seed


def verify_demo_password(password: str, password_hash: str) -> bool:
    algorithm, iterations, salt_text, digest_text = password_hash.split("$")
    if algorithm != "pbkdf2_sha256":
        return False
    salt = base64.urlsafe_b64decode(salt_text + "==")
    expected_digest = base64.urlsafe_b64decode(digest_text + "=")
    actual_digest = hashlib.pbkdf2_hmac(
        "sha256",
        password.encode("utf-8"),
        salt,
        int(iterations),
    )
    return hmac.compare_digest(actual_digest, expected_digest)


@pytest.fixture
def isolated_database(tmp_path, monkeypatch):
    from backend.app import database
    from backend.app.config import get_settings

    original_bind = database.SessionLocal.kw["bind"]
    database_path = tmp_path / "fieldassist-seed-test.db"
    test_engine = create_engine(
        f"sqlite:///{database_path.as_posix()}",
        connect_args={"check_same_thread": False},
    )
    monkeypatch.setattr(database, "engine", test_engine)
    database.SessionLocal.configure(bind=test_engine)
    monkeypatch.setenv("DEMO_ADMIN_PASSWORD", "configured-demo-password")
    get_settings.cache_clear()

    yield database, test_engine

    database.SessionLocal.configure(bind=original_bind)
    test_engine.dispose()
    get_settings.cache_clear()


def test_init_db_creates_all_required_tables_and_foreign_keys(isolated_database) -> None:
    database, test_engine = isolated_database
    load_task_modules()

    database.init_db()

    schema = inspect(test_engine)
    assert set(schema.get_table_names()) == EXPECTED_TABLES
    for table_name, expected_columns in EXPECTED_COLUMNS.items():
        assert {column["name"] for column in schema.get_columns(table_name)} == expected_columns
    for table_name, expected_keys in EXPECTED_FOREIGN_KEYS.items():
        actual_keys = {
            (foreign_key["constrained_columns"][0], foreign_key["referred_table"])
            for foreign_key in schema.get_foreign_keys(table_name)
        }
        assert actual_keys == expected_keys


def test_seed_database_is_complete_secure_and_idempotent(isolated_database) -> None:
    database, _ = isolated_database
    models, seed = load_task_modules()
    database.init_db()

    seed.seed_database()
    with database.SessionLocal() as session:
        users = session.scalars(select(models.User).order_by(models.User.email)).all()
        documents = session.scalars(select(models.KnowledgeDocument)).all()
        cases = session.scalars(select(models.EvaluationCase)).all()
        first_hashes = {user.email: user.password_hash for user in users}

    assert [(user.email, user.role) for user in users] == [
        ("admin@fieldassist.local", "admin"),
        ("employee@fieldassist.local", "employee"),
    ]
    assert all(user.is_active for user in users)
    assert len(documents) >= 5
    assert len(cases) >= 5
    assert {document.category for document in documents} >= {
        "customer_support_escalation",
        "data_security",
        "expense_reimbursement",
        "it_account_access",
        "leave_policy",
    }
    assert {case.category for case in cases} >= {
        "customer_support_escalation",
        "data_security",
        "expense_reimbursement",
        "it_account_access",
        "leave_policy",
    }
    assert all(password_hash != "configured-demo-password" for password_hash in first_hashes.values())
    assert all(password_hash.startswith("pbkdf2_sha256$600000$") for password_hash in first_hashes.values())
    assert all(
        verify_demo_password("configured-demo-password", password_hash)
        for password_hash in first_hashes.values()
    )
    assert len(set(first_hashes.values())) == 2

    seed.seed_database()
    with database.SessionLocal() as session:
        users_after = session.scalars(select(models.User)).all()
        documents_after = session.scalars(select(models.KnowledgeDocument)).all()
        cases_after = session.scalars(select(models.EvaluationCase)).all()

    assert len(users_after) == 2
    assert len(documents_after) == len(documents)
    assert len(cases_after) == len(cases)
    assert {user.email: user.password_hash for user in users_after} == first_hashes
