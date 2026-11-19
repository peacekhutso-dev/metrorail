from fastapi import FastAPI

from metrorail_common.app import create_app
from metrorail_common.clients import ServiceClient
from metrorail_common.config import ServiceSettings

from .api import router
from .models import Base


class Settings(ServiceSettings):
    service_name: str = "tracking"


async def _clients(app: FastAPI) -> None:
    if not hasattr(app.state, "network"):
        app.state.network = ServiceClient("network")
    app.state.trip_cache = {}


def build_app(settings: Settings | None = None, network=None) -> FastAPI:
    app = create_app(settings or Settings(), [router], base=Base, on_startup=[_clients])
    if network is not None:
        app.state.network = network
    return app


app = build_app()
