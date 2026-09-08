from __future__ import annotations

import json
import time
from typing import Any

from sqlalchemy import select
from sqlalchemy.orm import Session

from backend.app.config import Settings, get_settings
from backend.app.integrations.contracts import IntegrationError
from backend.app.integrations.factory import create_chat_provider
from backend.app.models import EvaluationCase, EvaluationResult, EvaluationRun, utc_now


def _safe_error(error: Exception) -> str:
    if isinstance(error, IntegrationError):
        return error.safe_message
    return IntegrationError.safe_message


def _expected_keywords(case: EvaluationCase) -> list[str]:
    try:
        value = json.loads(case.expected_keywords_json or "[]")
    except (TypeError, ValueError):
        return []
    if not isinstance(value, list):
        return []
    return [item.strip() for item in value if isinstance(item, str) and item.strip()]


def _matched(answer: str, keywords: list[str]) -> bool:
    normalized_answer = answer.casefold()
    return bool(keywords) and all(keyword.casefold() in normalized_answer for keyword in keywords)


def _latency_ms(started_at: float, metadata: dict[str, Any]) -> int:
    value = metadata.get("latency_ms")
    if isinstance(value, int) and not isinstance(value, bool) and value >= 0:
        return value
    return max(0, int((time.perf_counter() - started_at) * 1000))


def run_evaluation(
    db: Session,
    provider_name: str | None = None,
    settings: Settings | None = None,
) -> EvaluationRun:
    configured = settings or get_settings()
    provider = create_chat_provider(db, configured)
    cases = db.scalars(select(EvaluationCase).order_by(EvaluationCase.id)).all()
    effective_provider = provider_name or getattr(provider, "provider", configured.ai_provider)
    if not isinstance(effective_provider, str) or not effective_provider.strip():
        effective_provider = configured.ai_provider.casefold()

    run = EvaluationRun(
        provider=effective_provider.strip(),
        total_cases=len(cases),
        passed_cases=0,
        score=0,
    )
    db.add(run)
    db.flush()

    for case in cases:
        started_at = time.perf_counter()
        answer = ""
        error_message: str | None = None
        metadata: dict[str, Any] = {}
        try:
            result = provider.chat(case.question, "evaluation", None)
            answer = result.answer if isinstance(result.answer, str) else ""
            metadata = result.raw_metadata if isinstance(result.raw_metadata, dict) else {}
        except Exception as error:
            error_message = _safe_error(error)

        matched = error_message is None and _matched(answer, _expected_keywords(case))
        if matched:
            run.passed_cases += 1
        db.add(
            EvaluationResult(
                run_id=run.id,
                case_id=case.id,
                answer=answer,
                matched=matched,
                latency_ms=_latency_ms(started_at, metadata),
                error_message=error_message,
            )
        )

    run.score = run.passed_cases / run.total_cases if run.total_cases else 0
    run.completed_at = utc_now()
    db.commit()
    db.refresh(run)
    return run


def get_evaluation_detail(db: Session, run_id: int) -> dict[str, Any] | None:
    run = db.get(EvaluationRun, run_id)
    if run is None:
        return None
    results = db.scalars(
        select(EvaluationResult)
        .where(EvaluationResult.run_id == run.id)
        .order_by(EvaluationResult.case_id, EvaluationResult.id)
    ).all()
    return {
        "run": {
            "id": run.id,
            "provider": run.provider,
            "total_cases": run.total_cases,
            "passed_cases": run.passed_cases,
            "score": run.score,
            "started_at": run.started_at,
            "completed_at": run.completed_at,
        },
        "results": [
            {
                "id": result.id,
                "case_id": result.case_id,
                "answer": result.answer,
                "matched": result.matched,
                "latency_ms": result.latency_ms,
                "error_message": result.error_message,
            }
            for result in results
        ],
    }
