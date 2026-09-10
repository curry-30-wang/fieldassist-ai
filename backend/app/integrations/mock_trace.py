from __future__ import annotations

import hashlib
from typing import Any

from backend.app.integrations.contracts import HealthResult, TraceContext


class MockTracer:
    def start_trace(self, name: str, user_id: str, input_text: str) -> TraceContext:
        payload = f"mock-trace|{name}|{user_id}|{input_text}"
        digest = hashlib.sha256(payload.encode("utf-8")).hexdigest()[:24]
        return TraceContext(trace_id=f"mock-{digest}", provider="mock")

    def record_generation(
        self,
        context: TraceContext,
        model: str | None,
        input_text: str,
        output_text: str,
        latency_ms: int,
        metadata: dict[str, Any],
    ) -> None:
        return None

    def record_score(
        self,
        trace_id: str,
        name: str,
        value: float,
        comment: str | None,
    ) -> None:
        return None

    def health_check(self) -> HealthResult:
        return HealthResult(configured=True, reachable=True)
