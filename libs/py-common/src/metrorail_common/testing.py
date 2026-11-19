"""Test helpers: spin a service up in-process with SQLite + in-memory events."""

from contextlib import asynccontextmanager

import httpx
from fastapi import FastAPI


@asynccontextmanager
async def client_for(app: FastAPI):
    async with app.router.lifespan_context(app):
        transport = httpx.ASGITransport(app=app)
        async with httpx.AsyncClient(transport=transport, base_url="http://test") as c:
            yield c


def auth(user_id: str = "u-test", roles: str = "commuter") -> dict[str, str]:
    return {"Authorization": f"Bearer dev:{user_id}:{roles}"}
