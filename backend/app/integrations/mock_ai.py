from __future__ import annotations

import hashlib
import re
import unicodedata

from sqlalchemy import select
from sqlalchemy.orm import Session

from backend.app.integrations.contracts import ChatResult, HealthResult
from backend.app.models import KnowledgeDocument


NO_EVIDENCE_ANSWER = "当前知识库没有足够依据回答这个问题，请联系管理员补充相关资料。"
MOCK_MODEL = "deterministic-knowledge-match"
_TERM_PATTERN = re.compile(r"[a-z0-9]+|[\u3400-\u4dbf\u4e00-\u9fff]+")


def _normalized_text(value: str) -> str:
    normalized = unicodedata.normalize("NFKC", value).casefold()
    return "".join(character for character in normalized if character.isalnum())


def _keywords(value: str) -> set[str]:
    normalized = unicodedata.normalize("NFKC", value).casefold()
    terms: set[str] = set()
    for token in _TERM_PATTERN.findall(normalized):
        if token.isascii():
            terms.add(token)
            continue
        for width in range(2, min(4, len(token)) + 1):
            terms.update(token[index : index + width] for index in range(len(token) - width + 1))
    return terms


def _field_score(question: str, question_keywords: set[str], value: str, weight: int) -> int:
    normalized_value = _normalized_text(value)
    exact_score = weight if normalized_value and normalized_value in question else 0
    return exact_score + weight * len(question_keywords & _keywords(value))


def _trace_id(question: str, user_id: str, conversation_id: str | None) -> str:
    payload = f"mock-chat|{user_id}|{conversation_id or ''}|{_normalized_text(question)}"
    return f"mock-{hashlib.sha256(payload.encode('utf-8')).hexdigest()[:24]}"


class MockChatProvider:
    def __init__(self, db: Session) -> None:
        self._db = db

    def chat(
        self,
        question: str,
        user_id: str,
        conversation_id: str | None,
    ) -> ChatResult:
        normalized_question = _normalized_text(question)
        question_keywords = _keywords(question)
        documents = self._db.scalars(
            select(KnowledgeDocument)
            .where(KnowledgeDocument.enabled.is_(True))
            .order_by(KnowledgeDocument.id)
        ).all()

        scored_documents = [
            (
                _field_score(normalized_question, question_keywords, document.title, 5)
                + _field_score(normalized_question, question_keywords, document.category, 3)
                + _field_score(normalized_question, question_keywords, document.content, 1),
                document,
            )
            for document in documents
        ]
        score, document = max(scored_documents, key=lambda item: (item[0], -item[1].id), default=(0, None))
        trace_id = _trace_id(question, user_id, conversation_id)

        if score == 0 or document is None:
            return ChatResult(
                answer=NO_EVIDENCE_ANSWER,
                conversation_id=conversation_id,
                sources=[],
                provider="mock",
                model=MOCK_MODEL,
                raw_metadata={
                    "match_score": 0,
                    "matched_document_id": None,
                    "trace_id": trace_id,
                    "latency_ms": 0,
                },
            )

        source = {
            "document_id": document.id,
            "title": document.title,
            "category": document.category,
            "source_url": document.source_url,
        }
        return ChatResult(
            answer=f"根据《{document.title}》：{document.content}",
            conversation_id=conversation_id,
            sources=[source],
            provider="mock",
            model=MOCK_MODEL,
            raw_metadata={
                "match_score": score,
                "matched_document_id": document.id,
                "trace_id": trace_id,
                "latency_ms": 0,
            },
        )

    def health_check(self) -> HealthResult:
        return HealthResult(configured=True, reachable=True)
