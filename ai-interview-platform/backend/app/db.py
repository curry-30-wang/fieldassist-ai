from collections.abc import Callable

from sqlalchemy import Engine, create_engine
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
