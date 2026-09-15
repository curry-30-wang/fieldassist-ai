import fitz
import pytest

from app.services.document_parser import DocumentParser, split_text


def test_parser_reads_plain_text_and_rejects_empty_content():
    parser = DocumentParser()

    assert parser.parse("job.txt", "text/plain", "Python FastAPI".encode()) == "Python FastAPI"
    with pytest.raises(ValueError, match="empty"):
        parser.parse("empty.txt", "text/plain", b"   ")


def test_parser_reads_pdf_text():
    document = fitz.open()
    page = document.new_page()
    page.insert_text((72, 72), "FastAPI backend interview")
    pdf_bytes = document.tobytes()
    document.close()

    text = DocumentParser().parse("resume.pdf", "application/pdf", pdf_bytes)

    assert "FastAPI backend interview" in text


def test_split_text_keeps_order_and_overlap():
    chunks = split_text("abcdefghij", chunk_size=6, overlap=2)

    assert chunks == ["abcdef", "efghij"]
