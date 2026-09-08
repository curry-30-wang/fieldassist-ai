from __future__ import annotations

import logging
import unicodedata
import uuid
from typing import Any, Callable

from backend.app.config import Settings
from backend.app.integrations.contracts import HealthResult, TraceContext


logger = logging.getLogger(__name__)


class LangfuseTracer:
    """Langfuse v4 tracer with a safe local-context fallback."""

    def __init__(
        self,
        settings: Settings,
        client: Any | None = None,
        client_factory: Callable[[Settings], Any] | None = None,
    ) -> None:
        self.settings = settings
        self._configured = bool(
            settings.langfuse_public_key and settings.langfuse_secret_key
        )
        self._client: Any | None = client
        self._client_factory = client_factory or self._default_client_factory
        self._client_init_failed = False
        if self._configured and self._client is None:
            try:
                self._client = self._client_factory(settings)
            except Exception:
                self._client_init_failed = True
                logger.warning("langfuse_client_init_failed")

    @staticmethod
    def _default_client_factory(settings: Settings) -> Any:
        from langfuse import Langfuse

        return Langfuse(
            public_key=settings.langfuse_public_key,
            secret_key=settings.langfuse_secret_key,
            host=settings.langfuse_base_url,
        )

    @staticmethod
    def _new_trace_id() -> str:
        return uuid.uuid4().hex

    def start_trace(self, name: str, user_id: str, input_text: str) -> TraceContext:
        trace_id = self._new_trace_id()
        if self._client is not None:
            try:
                observation = self._client.start_observation(
                    name=name,
                    as_type="span",
                    input=input_text,
                    metadata={"user_id": user_id},
                )
                remote_trace_id = getattr(observation, "trace_id", None)
                if isinstance(remote_trace_id, str) and len(remote_trace_id) == 32:
                    trace_id = remote_trace_id
            except Exception:
                logger.warning("langfuse_trace_start_failed")
        return TraceContext(trace_id=trace_id, provider="langfuse")

    def record_generation(
        self,
        context: TraceContext,
        model: str | None,
        input_text: str,
        output_text: str,
        latency_ms: int,
        metadata: dict[str, Any],
    ) -> None:
        if self._client is None:
            return
        try:
            observation = self._client.start_observation(
                name="chat.generation",
                as_type="generation",
                input=input_text,
                output=output_text,
                model=model,
                metadata={**metadata, "latency_ms": latency_ms},
                trace_context={"trace_id": context.trace_id},
            )
            observation.end()
            self._client.flush()
        except Exception:
            logger.warning("langfuse_generation_failed")

    @staticmethod
    def _safe_comment(comment: str | None) -> str | None:
        if comment is None:
            return None
        return "".join(
            character
            for character in comment
            if not character.isspace()
            and not unicodedata.category(character).startswith("C")
        )[:500]

    def record_score(
        self,
        trace_id: str,
        name: str,
        value: float,
        comment: str | None,
    ) -> None:
        if self._client is None:
            return
        try:
            self._client.score(
                name=name,
                value=value,
                trace_id=trace_id,
                comment=self._safe_comment(comment),
            )
            self._client.flush()
        except Exception:
            logger.warning("langfuse_score_failed")

    def health_check(self) -> HealthResult:
        if not self._configured:
            return HealthResult(False, False, "not_configured")
        if self._client is None:
            return HealthResult(True, False, "client_init_failed")
        try:
            reachable = bool(self._client.auth_check())
            return HealthResult(
                configured=True,
                reachable=reachable,
                error_code=None if reachable else "health_check_failed",
            )
        except Exception:
            logger.warning("langfuse_health_check_failed")
            return HealthResult(True, False, "health_check_failed")
