from fastapi import FastAPI

from metrorail_common.app import create_app
from metrorail_common.config import ServiceSettings

from . import api
from .models import Base


class Settings(ServiceSettings):
    service_name: str = "notifications"


async def _subscribe(app: FastAPI) -> None:
    bus = app.state.bus

    def h(fn):
        async def _h(_subject, payload):
            await fn(app, payload)
        return _h

    await bus.subscribe("trips.trip.saved", h(api.on_trip_saved))
    await bus.subscribe("trips.trip.deleted", h(api.on_trip_deleted))
    await bus.subscribe("identity.user.deleted", h(api.on_user_deleted))
    await bus.subscribe("tracking.train.state_changed", h(api.on_train_event))


def build_app(settings: Settings | None = None) -> FastAPI:
    return create_app(settings or Settings(), [api.router], base=Base, on_startup=[_subscribe])


app = build_app()
