import base64
import hashlib
import json
from collections.abc import Iterator

import pytest
from fastapi import HTTPException
from fastapi.testclient import TestClient
from itsdangerous import TimestampSigner
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from backend.app.database import get_db
from backend.app.dependencies import require_admin
from backend.app.models import Base, User
from backend.app.security import hash_password, verify_password


DEMO_PASSWORD = "configured-demo-password"
TEST_SECRET_KEY = "task-3-test-secret"


def task_2_compatible_hash(password: str, salt: bytes) -> str:
    digest = hashlib.pbkdf2_hmac("sha256", password.encode("utf-8"), salt, 600_000)
    encode = lambda value: base64.urlsafe_b64encode(value).rstrip(b"=").decode("ascii")
    return f"pbkdf2_sha256$600000${encode(salt)}${encode(digest)}"


@pytest.fixture
def auth_client(tmp_path, monkeypatch) -> Iterator[TestClient]:
    from backend.app.config import get_settings
    from backend.app.main import create_app

    database_path = tmp_path / "fieldassist-auth-test.db"
    monkeypatch.setenv("SECRET_KEY", TEST_SECRET_KEY)
    get_settings.cache_clear()

    engine = create_engine(
        f"sqlite:///{database_path.as_posix()}",
        connect_args={"check_same_thread": False},
    )
    testing_session = sessionmaker(bind=engine, autoflush=False, autocommit=False)
    Base.metadata.create_all(bind=engine)
    with testing_session.begin() as db:
        db.add_all(
            (
                User(
                    email="admin@fieldassist.local",
                    display_name="Demo Admin",
                    password_hash=task_2_compatible_hash(DEMO_PASSWORD, bytes(range(16))),
                    role="admin",
                    is_active=True,
                ),
                User(
                    email="employee@fieldassist.local",
                    display_name="Demo Employee",
                    password_hash=task_2_compatible_hash(DEMO_PASSWORD, bytes(range(16, 32))),
                    role="employee",
                    is_active=True,
                ),
                User(
                    email="inactive@fieldassist.local",
                    display_name="Inactive Employee",
                    password_hash=task_2_compatible_hash(DEMO_PASSWORD, bytes(range(32, 48))),
                    role="employee",
                    is_active=False,
                ),
            )
        )

    def override_get_db() -> Iterator[Session]:
        with testing_session() as db:
            yield db

    application = create_app()
    application.dependency_overrides[get_db] = override_get_db
    with TestClient(application) as client:
        yield client

    application.dependency_overrides.clear()
    engine.dispose()
    get_settings.cache_clear()


def assert_safe_user_profile(payload: dict, email: str, display_name: str, role: str) -> None:
    assert payload["success"] is True
    assert payload["data"]["user"] == {
        "id": payload["data"]["user"]["id"],
        "email": email,
        "display_name": display_name,
        "role": role,
        "is_active": True,
    }
    assert isinstance(payload["data"]["user"]["id"], int)
    assert "password" not in json.dumps(payload, ensure_ascii=False).lower()
    assert TEST_SECRET_KEY not in json.dumps(payload, ensure_ascii=False)


def test_admin_login_creates_session_containing_only_user_id(auth_client: TestClient) -> None:
    response = auth_client.post(
        "/api/auth/login",
        json={"email": "admin@fieldassist.local", "password": DEMO_PASSWORD},
    )

    assert response.status_code == 200
    assert response.json()["message"] == "登录成功"
    assert_safe_user_profile(response.json(), "admin@fieldassist.local", "Demo Admin", "admin")
    signed_cookie = auth_client.cookies.get("session")
    assert signed_cookie is not None
    session_bytes = TimestampSigner(TEST_SECRET_KEY).unsign(signed_cookie, max_age=14 * 24 * 60 * 60)
    assert json.loads(base64.b64decode(session_bytes)) == {"user_id": response.json()["data"]["user"]["id"]}


def test_employee_can_login_and_read_current_profile(auth_client: TestClient) -> None:
    login = auth_client.post(
        "/api/auth/login",
        json={"email": "employee@fieldassist.local", "password": DEMO_PASSWORD},
    )
    profile = auth_client.get("/api/auth/me")

    assert login.status_code == 200
    assert_safe_user_profile(
        login.json(),
        "employee@fieldassist.local",
        "Demo Employee",
        "employee",
    )
    assert profile.status_code == 200
    assert profile.json()["message"] == "获取当前用户成功"
    assert_safe_user_profile(
        profile.json(),
        "employee@fieldassist.local",
        "Demo Employee",
        "employee",
    )


def test_wrong_password_returns_safe_chinese_401(auth_client: TestClient) -> None:
    response = auth_client.post(
        "/api/auth/login",
        json={"email": "admin@fieldassist.local", "password": "wrong-password"},
    )

    assert response.status_code == 401
    assert response.json() == {"success": False, "data": None, "message": "邮箱或密码错误"}


def test_unknown_email_still_runs_pbkdf2(auth_client: TestClient, monkeypatch) -> None:
    original_pbkdf2_hmac = hashlib.pbkdf2_hmac
    calls = []

    def recording_pbkdf2_hmac(hash_name, password, salt, iterations, dklen=None):
        calls.append((hash_name, password, salt, iterations, dklen))
        return original_pbkdf2_hmac(hash_name, password, salt, iterations, dklen)

    monkeypatch.setattr(hashlib, "pbkdf2_hmac", recording_pbkdf2_hmac)

    response = auth_client.post(
        "/api/auth/login",
        json={"email": "missing@fieldassist.local", "password": "unknown-user-password"},
    )

    assert response.status_code == 401
    assert response.json() == {"success": False, "data": None, "message": "邮箱或密码错误"}
    assert len(calls) == 1
    assert calls[0][0] == "sha256"
    assert len(calls[0][2]) == 16
    assert calls[0][3:] == (600_000, 32)


def test_malformed_login_does_not_echo_rejected_credentials(auth_client: TestClient) -> None:
    leaked_password = "must-not-appear-in-validation-response"

    response = auth_client.post("/api/auth/login", json={"password": leaked_password})

    assert response.status_code == 422
    assert response.json() == {
        "success": False,
        "data": None,
        "message": "请求参数格式错误",
    }
    assert leaked_password not in response.text


def test_inactive_user_is_rejected_with_same_safe_401(auth_client: TestClient) -> None:
    response = auth_client.post(
        "/api/auth/login",
        json={"email": "inactive@fieldassist.local", "password": DEMO_PASSWORD},
    )

    assert response.status_code == 401
    assert response.json() == {"success": False, "data": None, "message": "邮箱或密码错误"}


def test_unauthenticated_me_returns_safe_chinese_401(auth_client: TestClient) -> None:
    response = auth_client.get("/api/auth/me")

    assert response.status_code == 401
    assert response.json() == {"success": False, "data": None, "message": "请先登录"}


def test_logout_clears_session(auth_client: TestClient) -> None:
    auth_client.post(
        "/api/auth/login",
        json={"email": "admin@fieldassist.local", "password": DEMO_PASSWORD},
    )

    logout = auth_client.post("/api/auth/logout")
    profile = auth_client.get("/api/auth/me")

    assert logout.status_code == 200
    assert logout.json() == {"success": True, "data": None, "message": "退出登录成功"}
    assert "session" not in auth_client.cookies
    assert profile.status_code == 401


def test_public_placeholder_cannot_sign_a_session(monkeypatch) -> None:
    from backend.app.config import get_settings
    from backend.app.main import create_app

    monkeypatch.setenv("SECRET_KEY", "replace-with-a-local-secret")
    get_settings.cache_clear()
    payload = base64.b64encode(json.dumps({"user_id": 1}).encode("utf-8"))
    forged_cookie = TimestampSigner("replace-with-a-local-secret").sign(payload).decode("utf-8")

    with TestClient(create_app()) as client:
        client.cookies.set("session", forged_cookie)
        response = client.post("/api/auth/logout")

    assert "session=null" not in response.headers.get("set-cookie", "")
    get_settings.cache_clear()


def test_employee_is_rejected_by_admin_dependency() -> None:
    employee = User(
        email="employee@fieldassist.local",
        display_name="Demo Employee",
        password_hash="not-returned",
        role="employee",
        is_active=True,
    )

    with pytest.raises(HTTPException) as error:
        require_admin(employee)

    assert error.value.status_code == 403
    assert error.value.detail == "需要管理员权限"


def test_password_hashes_match_task_2_format_and_never_contain_cleartext() -> None:
    known_hash = task_2_compatible_hash(DEMO_PASSWORD, bytes(range(16)))

    assert verify_password(DEMO_PASSWORD, known_hash) is True
    assert verify_password("wrong-password", known_hash) is False

    first_hash = hash_password(DEMO_PASSWORD)
    second_hash = hash_password(DEMO_PASSWORD)
    assert first_hash != second_hash
    for password_hash in (first_hash, second_hash):
        algorithm, iterations, salt_text, digest_text = password_hash.split("$")
        assert algorithm == "pbkdf2_sha256"
        assert iterations == "600000"
        assert len(base64.urlsafe_b64decode(salt_text + "==")) == 16
        assert len(base64.urlsafe_b64decode(digest_text + "=")) == 32
        assert DEMO_PASSWORD not in password_hash
        assert verify_password(DEMO_PASSWORD, password_hash) is True


@pytest.mark.parametrize(
    "malformed_hash",
    (
        "cleartext",
        "pbkdf2_sha256$1$c2FsdA$ZGlnZXN0",
        "pbkdf2_sha256$600000$***$***",
        task_2_compatible_hash(DEMO_PASSWORD, b"short"),
    ),
)
def test_malformed_password_hashes_are_rejected_without_error(malformed_hash: str) -> None:
    assert verify_password(DEMO_PASSWORD, malformed_hash) is False
