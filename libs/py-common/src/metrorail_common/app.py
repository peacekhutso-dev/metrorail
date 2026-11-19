"""App factory: every service gets the same middleware, health checks, errors, DB and event bus."""

import uuid
from collections.abc import Awaitable, Callable, Sequence
from contextlib import asynccontextmanager

import structlog
from fastapi import APIRouter, FastAPI, Request
from fastapi.middleware.cors import CORSMiddleware

from . import __version__
from .config import ServiceSettings
from .db import Base, Database
from .errors import install_error_handlers
from .events import make_bus
from .logging import configure_logging, get_logger

Hook = Callable[[FastAPI], Awaitable[None]]


def create_app(
    settings: ServiceSettings,
    routers: Sequence[APIRouter] = (),
    *,
    base: type | None = Base,
    on_startup: Sequence[Hook] = (),
    on_shutdown: Sequence[Hook] = (),
    title: str | None = None,
) -> FastAPI:
    configure_logging(settings.service_name, settings.log_level)
    log = get_logger("app")

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        app.state.db = Database(settings.database_url)
        if base is not None and not settings.is_production:  # prod: Alembic migrations
            await app.state.db.create_all(base)
        app.state.bus = make_bus(settings.event_bus, settings.nats_url, settings.service_name)
        await app.state.bus.connect()
        for hook in on_startup:
            await hook(app)
        log.info("service_started", env=settings.environment)
        yield
        for hook in on_shutdown:
            await hook(app)
        await app.state.bus.close()
        await app.state.db.dispose()

    app = FastAPI(
        title=title or f"MetroRail {settings.service_name} service",
        version=__version__,
        lifespan=lifespan,
        docs_url="/docs",
        openapi_url="/openapi.json",
    )
    app.state.settings = settings
    app.add_middleware(
        CORSMiddleware, allow_origins=settings.cors_origins, allow_credentials=True,
        allow_methods=["*"], allow_headers=["*"],
    )

    @app.middleware("http")
    async def request_id(request: Request, call_next):
        rid = request.headers.get("x-request-id") or uuid.uuid4().hex
        request.state.request_id = rid
        structlog.contextvars.bind_contextvars(request_id=rid)
        try:
            response = await call_next(request)
        finally:
            structlog.contextvars.unbind_contextvars("request_id")
        response.headers["x-request-id"] = rid
        return response

    install_error_handlers(app)

    @app.get("/health", tags=["ops"])
    async def health():
        return {"status": "ok", "service": settings.service_name, "version": __version__}

    @app.get("/ready", tags=["ops"])
    async def ready(request: Request):
        from sqlalchemy import text

        async with request.app.state.db.sessionmaker() as s:
            await s.execute(text("SELECT 1"))
        return {"status": "ready"}

    for r in routers:
        app.include_router(r)
    return app


async def get_session(request: Request):
    async with request.app.state.db.sessionmaker() as s:
        yield s


def get_bus(request: Request):
    return request.app.state.bus
