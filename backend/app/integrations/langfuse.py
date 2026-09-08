from __future__ import annotations

import logging
import re
import unicodedata
import uuid
from typing import Any, Callable

from backend.app.config import Settings
from backend.app.integrations.contracts import HealthResult, TraceContext


logger = logging.getLogger(__name__)


class LangfuseTracer:
    """Langfuse v4 tracer with a safe local-context fallback."""

    _SAFE_TEXT_LIMIT = 2000
    _SAFE_METADATA_KEYS = frozenset({"provider", "request_id"})
    _EMAIL_PATTERN = re.compile(
        r"(?<![\w.+-])[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}(?![\w.-])",
        re.IGNORECASE,
    )
    _PHONE_PATTERN = re.compile(r"(?<!\d)(?:\+?86[- ]?)?1[3-9]\d{9}(?!\d)")
    _SECRET_PATTERN = re.compile(
        r"(?<![\w-])(?:password|token|secret|api[_-]?key)\s*[:=]\s*[^\s,;&]+",
        re.IGNORECASE,
    )

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
            base_url=settings.langfuse_base_url,
            timeout=settings.request_timeout_seconds,
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
                    input=self._safe_text(input_text),
                    metadata={"user_id": self._safe_text(user_id)},
                )
                remote_trace_id = getattr(observation, "trace_id", None)
                if isinstance(remote_trace_id, str) and len(remote_trace_id) == 32:
                    trace_id = remote_trace_id
                observation.end()
            except Exception:
                logger.warning("langfuse_trace_start_failed")
            finally:
                self._flush()
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
                input=self._safe_text(input_text),
                output=self._safe_text(output_text),
                model=model,
                metadata={
                    **self._safe_metadata(metadata),
                    "latency_ms": latency_ms,
                },
                trace_context={"trace_id": context.trace_id},
            )
            observation.end()
        except Exception:
            logger.warning("langfuse_generation_failed")
        finally:
            self._flush()

    def _flush(self) -> None:
        if self._client is None:
            return
        try:
            self._client.flush()
        except Exception:
            logger.warning("langfuse_flush_failed")

    @classmethod
    def _safe_text(cls, value: str, limit: int | None = None) -> str:
        text = "".join(
            " " if unicodedata.category(character).startswith("C") else character
            for character in value
        )
        text = cls._EMAIL_PATTERN.sub("[REDACTED_EMAIL]", text)
        text = cls._PHONE_PATTERN.sub("[REDACTED_PHONE]", text)
        text = cls._SECRET_PATTERN.sub("[REDACTED_SECRET]", text)
        return text[: limit or cls._SAFE_TEXT_LIMIT]

    @classmethod
    def _safe_metadata(cls, metadata: dict[str, Any]) -> dict[str, Any]:
        return {
            key: cls._safe_text(value)
            for key, value in metadata.items()
            if key in cls._SAFE_METADATA_KEYS and isinstance(value, str)
        }

    @staticmethod
    def _safe_comment(comment: str | None) -> str | None:
        if comment is None:
            return None
        return LangfuseTracer._safe_text(comment, limit=500)

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
            self._client.create_score(
                name=name,
                value=value,
                trace_id=trace_id,
                comment=self._safe_comment(comment),
                data_type="NUMERIC",
            )
        except Exception:
            logger.warning("langfuse_score_failed")
        finally:
            self._flush()

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
