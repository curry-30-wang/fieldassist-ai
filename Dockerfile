FROM python:3.12-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt \
    && useradd --create-home --shell /usr/sbin/nologin appuser

COPY --chown=appuser:appuser backend ./backend
COPY --chown=appuser:appuser frontend ./frontend
COPY --chown=appuser:appuser database ./database
COPY --chown=appuser:appuser docs ./docs

USER appuser

EXPOSE 8000

CMD ["python", "backend/run.py"]
