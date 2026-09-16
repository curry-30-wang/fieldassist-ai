from collections.abc import Callable

from sqlalchemy import Engine, Index, MetaData, Table, create_engine, inspect
from sqlalchemy.orm import DeclarativeBase, Session, sessionmaker
from sqlalchemy.pool import NullPool


class Base(DeclarativeBase):
    pass


def create_engine_and_session(
    database_url: str,
) -> tuple[Engine, Callable[[], Session]]:
    is_file_sqlite = database_url.startswith("sqlite:") and ":memory:" not in database_url
    engine = create_engine(
        database_url,
        **({"poolclass": NullPool} if is_file_sqlite else {}),
    )
    return engine, sessionmaker(bind=engine, expire_on_commit=False)


def init_db(engine: Engine) -> None:
    from app import models  # noqa: F401

    Base.metadata.create_all(bind=engine)
    _ensure_unique_index(
        engine,
        "questions",
        "uq_questions_session_order",
        ("session_id", "order_index"),
    )
    _ensure_unique_index(
        engine,
        "answers",
        "uq_answers_question_id",
        ("question_id",),
    )
    _ensure_unique_index(
        engine,
        "reports",
        "uq_reports_session_id",
        ("session_id",),
    )


def _ensure_unique_index(
    engine: Engine,
    table_name: str,
    index_name: str,
    column_names: tuple[str, ...],
) -> None:
    inspector = inspect(engine)
    unique_columns = {
        tuple(item["column_names"])
        for item in (
            inspector.get_unique_constraints(table_name)
            + inspector.get_indexes(table_name)
        )
        if item.get("unique") is not False
    }
    if column_names in unique_columns:
        return

    metadata = MetaData()
    table = Table(table_name, metadata, autoload_with=engine)
    Index(
        index_name,
        *(table.c[column_name] for column_name in column_names),
        unique=True,
    ).create(bind=engine, checkfirst=True)
