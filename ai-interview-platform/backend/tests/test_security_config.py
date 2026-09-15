import re
from pathlib import Path

from fastapi.testclient import TestClient

import app.main as main_module
from app.services.llm import FakeLLMProvider


PROJECT_ROOT = Path(__file__).parents[2]


def test_acceptance_documentation_and_samples_exist():
    readme = PROJECT_ROOT / "README.md"
    evals = PROJECT_ROOT / "evals" / "README.md"
    resume = PROJECT_ROOT / "sample_data" / "resume.txt"
    job_description = PROJECT_ROOT / "sample_data" / "job_description.txt"
    env_example = PROJECT_ROOT / "backend" / ".env.example"

    for path in (readme, evals, resume, job_description, env_example):
        assert path.exists(), path

    readme_text = readme.read_text(encoding="utf-8")
    for command in (
        r"backend\.venv\Scripts\python.exe -m pytest backend -q",
        "uvicorn app.main:app --reload --port 8000",
        "npm install",
        "npm run dev",
        "npm run test -- --run",
        "npm run build",
    ):
        assert command in readme_text

    for setting_name in ("LLM_BASE_URL", "LLM_API_KEY", "LLM_MODEL"):
        assert setting_name in readme_text
        assert setting_name in env_example.read_text(encoding="utf-8")
    assert "FakeLLMProvider" in readme_text

    evals_text = evals.read_text(encoding="utf-8")
    for field_name in (
        "样例编号",
        "岗位类型",
        "资料是否成功解析",
        "题目数量",
        "评分 JSON 是否合法",
        "人工检查备注",
    ):
        assert field_name in evals_text

    assert "虚构" in resume.read_text(encoding="utf-8")
    assert "虚构" in job_description.read_text(encoding="utf-8")


def test_repository_does_not_store_real_api_keys():
    excluded_parts = {
        ".git",
        ".venv",
        "__pycache__",
        ".pytest_cache",
        "data",
        "dist",
        "node_modules",
    }
    source_files = [
        path
        for path in PROJECT_ROOT.rglob("*")
        if path.is_file() and not excluded_parts.intersection(path.parts)
    ]
    forbidden_patterns = (
        re.compile("s" + r"k-[A-Za-z0-9]{16,}"),
        re.compile("LLM_API_KEY" + r"\s*=\s*s" + r"k-", re.IGNORECASE),
        re.compile("gh" + r"[oprsu]_[A-Za-z0-9]{20,}"),
        re.compile("AK" + r"IA[0-9A-Z]{16}"),
    )

    for path in source_files:
        text = path.read_text(encoding="utf-8", errors="ignore")
        assert not any(pattern.search(text) for pattern in forbidden_patterns), path


def test_create_app_creates_backend_data_directory(tmp_path, monkeypatch):
    expected_data_dir = tmp_path / "backend" / "data"
    monkeypatch.setattr(main_module, "BACKEND_DATA_DIR", expected_data_dir)

    main_module.create_app(
        database_url="sqlite:///:memory:",
        llm_provider=FakeLLMProvider(),
    )

    assert expected_data_dir.is_dir()


def test_cors_only_allows_local_vite_origins():
    application = main_module.create_app(
        database_url="sqlite:///:memory:",
        llm_provider=FakeLLMProvider(),
    )
    client = TestClient(application)

    for origin in ("http://localhost:5173", "http://127.0.0.1:5173"):
        response = client.options(
            "/api/health",
            headers={
                "Origin": origin,
                "Access-Control-Request-Method": "GET",
            },
        )
        assert response.status_code == 200
        assert response.headers["access-control-allow-origin"] == origin

    response = client.options(
        "/api/health",
        headers={
            "Origin": "https://example.test",
            "Access-Control-Request-Method": "GET",
        },
    )
    assert "access-control-allow-origin" not in response.headers
