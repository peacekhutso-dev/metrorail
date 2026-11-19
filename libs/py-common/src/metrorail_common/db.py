"""Async SQLAlchemy setup. Each service owns its own database/schema (no shared tables)."""

from collections.abc import AsyncIterator
from datetime import UTC, datetime

from sqlalchemy import DateTime
from sqlalchemy.ext.asyncio import (
    AsyncEngine,
    AsyncSession,
    async_sessionmaker,
    create_async_engine,
)
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column
from sqlalchemy.pool import StaticPool


def utcnow() -> datetime:
    return datetime.now(UTC)


class Base(DeclarativeBase):
    pass


class TimestampMixin:
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=utcnow)
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), default=utcnow, onupdate=utcnow
    )


class Database:
    def __init__(self, url: str):
        kwargs: dict = {}
        if url.startswith("sqlite"):
            # One shared in-memory DB across the app's sessions (tests / quick local runs).
            kwargs = {"connect_args": {"check_same_thread": False}, "poolclass": StaticPool}
        self.engine: AsyncEngine = create_async_engine(url, **kwargs)
        self.sessionmaker = async_sessionmaker(self.engine, expire_on_commit=False)

    async def create_all(self, base: type[DeclarativeBase] = Base) -> None:
        """Dev/test convenience. Production uses Alembic migrations in each service."""
        async with self.engine.begin() as conn:
            await conn.run_sync(base.metadata.create_all)

    async def session(self) -> AsyncIterator[AsyncSession]:
        async with self.sessionmaker() as s:
            yield s

    async def dispose(self) -> None:
        await self.engine.dispose()
