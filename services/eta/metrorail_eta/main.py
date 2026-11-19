from fastapi import FastAPI

from metrorail_common.app import create_app
from metrorail_common.clients import ServiceClient
from metrorail_common.config import ServiceSettings

from .api import router
from .predictor import load_predictor


class Settings(ServiceSettings):
    service_name: str = "eta"
    model_uri: str | None = None  # ETA_MODEL_URI, e.g. models:/eta/Production


async def _init(app: FastAPI) -> None:
    if not hasattr(app.state, "network"):
        app.state.network = ServiceClient("network")
    if not hasattr(app.state, "tracking"):
        app.state.tracking = ServiceClient("tracking")
    app.state.predictor = load_predictor(app.state.settings.model_uri)


def build_app(settings: Settings | None = None, network=None, tracking=None) -> FastAPI:
    app = create_app(settings or Settings(), [router], base=None, on_startup=[_init])
    if network is not None:
        app.state.network = network
    if tracking is not None:
        app.state.tracking = tracking
    return app


app = build_app()
