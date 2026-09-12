from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[1]


def read_project_file(relative_path: str) -> str:
    return (PROJECT_ROOT / relative_path).read_text(encoding="utf-8")


def test_readme_documents_reproducible_mock_and_real_setup() -> None:
    readme = read_project_file("README.md")

    for required_text in (
        "python -m venv .venv",
        "backend\\seed.py",
        "backend\\run.py",
        "docker compose up --build",
        "pytest",
        "8000",
        "employee@fieldassist.local",
        "admin@fieldassist.local",
        "AI_PROVIDER",
        "DIFY_API_KEY",
        "LANGFUSE_SECRET_KEY",
        "不要提交",
    ):
        assert required_text in readme


def test_handoff_docs_cover_integrations_and_safe_secret_boundary() -> None:
    for relative_path in (
        "docs/architecture.md",
        "docs/api.md",
        "docs/demo-script.md",
        "docs/runbook.md",
        "docs/resume.md",
        "docs/n8n-extension.md",
    ):
        assert (PROJECT_ROOT / relative_path).exists(), relative_path

    architecture = read_project_file("docs/architecture.md")
    runbook = read_project_file("docs/runbook.md")
    extension = read_project_file("docs/n8n-extension.md")
    for document in (architecture, runbook):
        for required_text in ("Dify", "Langfuse", "Mock", "密钥"):
            assert required_text in document
    assert "不依赖 n8n" in extension
    assert "webhook" in extension.lower()


def test_resume_material_uses_evidence_backed_values() -> None:
    resume = read_project_file("docs/resume.md")

    assert "[待替换" not in resume
    assert "111" in resume
    assert "5/5" in resume
    assert "Mock" in resume
    assert "中文" in resume
    assert "English" in resume


def test_seed_entrypoint_runs_from_project_root(tmp_path: Path) -> None:
    database_path = tmp_path / "seed-entrypoint.db"
    environment = os.environ.copy()
    environment["DATABASE_URL"] = f"sqlite:///{database_path.as_posix()}"
    environment["DEMO_ADMIN_PASSWORD"] = "delivery-test-password"

    result = subprocess.run(
        [sys.executable, str(PROJECT_ROOT / "backend" / "seed.py")],
        cwd=PROJECT_ROOT,
        env=environment,
        capture_output=True,
        text=True,
        timeout=30,
    )

    assert result.returncode == 0, result.stderr
    assert database_path.exists()
