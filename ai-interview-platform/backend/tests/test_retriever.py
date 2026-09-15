from app.services.retriever import ChunkRecord, TfidfRetriever


def test_retriever_returns_most_relevant_chunk_first():
    retriever = TfidfRetriever()
    retriever.index([
        ChunkRecord("resume", 0, "熟悉 Python 和 FastAPI 接口开发"),
        ChunkRecord("resume", 1, "负责网页视觉样式设计"),
    ])

    results = retriever.search("Python FastAPI", top_k=1)

    assert len(results) == 1
    assert results[0].chunk_index == 0
    assert results[0].score > 0


def test_retriever_handles_records_without_usable_ngrams():
    retriever = TfidfRetriever()

    retriever.index([
        ChunkRecord("resume", 0, "甲"),
    ])

    assert retriever.search("甲") == []
