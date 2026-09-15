import pytest
from sqlalchemy import inspect
from sqlalchemy.exc import IntegrityError

from app.db import create_engine_and_session, init_db
from app.domain import InterviewStatus, can_transition


def test_created_session_can_start():
    assert can_transition(InterviewStatus.CREATED, InterviewStatus.IN_PROGRESS)


def test_completed_session_cannot_return_to_progress():
    assert not can_transition(InterviewStatus.COMPLETED, InterviewStatus.IN_PROGRESS)


def test_init_db_creates_all_interview_tables_without_removing_existing_tables():
    engine, _ = create_engine_and_session("sqlite:///:memory:")

    init_db(engine)
    first_table_names = set(inspect(engine).get_table_names())
    init_db(engine)

    assert first_table_names == {
        "answers",
        "document_chunks",
        "documents",
        "interview_sessions",
        "operation_claims",
        "questions",
        "reports",
    }
    assert set(inspect(engine).get_table_names()) == first_table_names


def test_init_db_adds_uniqueness_to_existing_interview_tables():
    engine, _ = create_engine_and_session("sqlite:///:memory:")
    with engine.begin() as connection:
        connection.exec_driver_sql(
            "CREATE TABLE questions ("
            "id TEXT PRIMARY KEY, session_id TEXT NOT NULL, order_index INTEGER NOT NULL)"
        )
        connection.exec_driver_sql(
            "CREATE TABLE answers (id TEXT PRIMARY KEY, question_id TEXT NOT NULL)"
        )

    init_db(engine)

    with engine.begin() as connection:
        connection.exec_driver_sql(
            "INSERT INTO questions (id, session_id, order_index) VALUES "
            "('q1', 'session-1', 0)"
        )
        with pytest.raises(IntegrityError):
            connection.exec_driver_sql(
                "INSERT INTO questions (id, session_id, order_index) VALUES "
                "('q2', 'session-1', 0)"
            )

    with engine.begin() as connection:
        connection.exec_driver_sql(
            "INSERT INTO answers (id, question_id) VALUES ('a1', 'q1')"
        )
        with pytest.raises(IntegrityError):
            connection.exec_driver_sql(
                "INSERT INTO answers (id, question_id) VALUES ('a2', 'q1')"
            )
