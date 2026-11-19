from fastapi import FastAPI

from metrorail_common.app import create_app
from metrorail_common.config import ServiceSettings

from .api import purge_user, router
from .models import Base


class Settings(ServiceSettings):
    service_name: str = "trips"


async def _subscribe(app: FastAPI) -> None:
    async def on_user_deleted(_subject, payload):
        await purge_user(app, payload["user_id"])

    await app.state.bus.subscribe("identity.user.deleted", on_user_deleted)


def build_app(settings: Settings | None = None) -> FastAPI:
    return create_app(settings or Settings(), [router], base=Base, on_startup=[_subscribe])


app = build_app()
