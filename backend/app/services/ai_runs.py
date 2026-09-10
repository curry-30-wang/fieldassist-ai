from __future__ import annotations

import json
from typing import Any

from sqlalchemy.orm import Session

from backend.app.integrations.contracts import ChatResult
from backend.app.models import AiRun, Message


def _optional_value(metadata: dict[str, Any], name: str, expected_type: type) -> Any:
    value = metadata.get(name)
    if isinstance(value, bool) or not isinstance(value, expected_type):
        return None
    return value


def record_ai_run(db: Session, message_id: int, result: ChatResult) -> AiRun:
    metadata = result.raw_metadata
    message = db.get(Message, message_id)
    if message is None:
        raise ValueError("关联消息不存在")
    message.sources_json = json.dumps(result.sources, ensure_ascii=False, separators=(",", ":"))
    run = AiRun(
        message_id=message_id,
        provider=result.provider,
        status="success",
        trace_id=_optional_value(metadata, "trace_id", str),
        model=result.model,
        latency_ms=_optional_value(metadata, "latency_ms", int),
        input_tokens=_optional_value(metadata, "input_tokens", int),
        output_tokens=_optional_value(metadata, "output_tokens", int),
        total_cost=_optional_value(metadata, "total_cost", (int, float)),
    )
    db.add(run)
    db.commit()
    db.refresh(run)
    return run


def record_ai_failure(
    db: Session,
    message_id: int,
    provider: str,
    error_code: str,
    latency_ms: int,
) -> AiRun:
    run = AiRun(
        message_id=message_id,
        provider=provider,
        status="failed",
        latency_ms=latency_ms,
        error_code=error_code,
    )
    db.add(run)
    db.commit()
    db.refresh(run)
    return run
