import io
from pathlib import Path

import fitz
from docx import Document


MIME_TYPE_SUFFIXES = {
    "text/plain": ".txt",
    "application/pdf": ".pdf",
    "application/vnd.openxmlformats-officedocument.wordprocessingml.document": ".docx",
}


class DocumentParser:
    """Extract text from supported interview reference documents."""

    def parse(self, filename: str, content_type: str, data: bytes) -> str:
        suffix = Path(filename).suffix.lower()
        mime_suffix = MIME_TYPE_SUFFIXES.get(content_type)

        if suffix not in {".txt", ".pdf", ".docx"} or (
            mime_suffix is not None and mime_suffix != suffix
        ):
            raise ValueError("unsupported file type")

        if suffix == ".txt":
            text = data.decode("utf-8")
        elif suffix == ".pdf":
            with fitz.open(stream=data, filetype="pdf") as document:
                text = "\n".join(page.get_text() for page in document)
        else:
            text = "\n".join(paragraph.text for paragraph in Document(io.BytesIO(data)).paragraphs)

        text = text.strip()
        if not text:
            raise ValueError("empty document")
        return text


def split_text(text: str, chunk_size: int = 800, overlap: int = 100) -> list[str]:
    if not 0 <= overlap < chunk_size:
        raise ValueError("overlap must satisfy 0 <= overlap < chunk_size")
    if not text:
        return []

    step = chunk_size - overlap
    chunks = []
    for start in range(0, len(text), step):
        chunks.append(text[start : start + chunk_size])
        if start + chunk_size >= len(text):
            break
    return chunks
