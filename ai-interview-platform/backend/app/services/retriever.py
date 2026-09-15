from dataclasses import dataclass
from typing import Protocol, Sequence

from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.metrics.pairwise import cosine_similarity


@dataclass(frozen=True)
class ChunkRecord:
    document_id: str
    chunk_index: int
    text: str


@dataclass(frozen=True)
class RetrievedChunk:
    document_id: str
    chunk_index: int
    text: str
    score: float


class Retriever(Protocol):
    def index(self, records: Sequence[ChunkRecord]) -> None: ...

    def search(self, query: str, top_k: int = 4) -> list[RetrievedChunk]: ...


class TfidfRetriever:
    def __init__(self) -> None:
        self._records: list[ChunkRecord] = []
        self._vectorizer = TfidfVectorizer(analyzer="char", ngram_range=(2, 4))
        self._matrix = None

    def index(self, records: Sequence[ChunkRecord]) -> None:
        self._records = list(records)
        self._matrix = (
            self._vectorizer.fit_transform(record.text for record in self._records)
            if self._records
            else None
        )

    def search(self, query: str, top_k: int = 4) -> list[RetrievedChunk]:
        if not self._records or self._matrix is None or top_k <= 0:
            return []

        query_vector = self._vectorizer.transform([query])
        scores = cosine_similarity(query_vector, self._matrix).ravel()
        ranked = sorted(
            ((score, record) for score, record in zip(scores, self._records) if score > 0),
            key=lambda item: item[0],
            reverse=True,
        )
        return [
            RetrievedChunk(record.document_id, record.chunk_index, record.text, float(score))
            for score, record in ranked[:top_k]
        ]
