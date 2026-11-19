from fastapi import FastAPI

from metrorail_common.app import create_app
from metrorail_common.config import ServiceSettings

from . import api
from .centrifugo import Centrifugo


class Settings(ServiceSettings):
    service_name: str = "realtime"
    centrifugo_api_url: str = "http://centrifugo:8000"
    centrifugo_api_key: str = "dev-api-key"
    centrifugo_token_secret: str = "dev-token-secret-change-me-32-bytes!!"
    centrifugo_public_url: str = "http://localhost:8001"


async def _init(app: FastAPI) -> None:
    st: Settings = app.state.settings
    if not hasattr(app.state, "centrifugo"):
        app.state.centrifugo = Centrifugo(st.centrifugo_api_url, st.centrifugo_api_key)

    async def fwd(subject, ev):
        await api.forward(app, subject, ev)

    for subject in ("tracking.train.position", "tracking.train.state_changed",
                    "notifications.alert.created"):
        await app.state.bus.subscribe(subject, fwd)


def build_app(settings: Settings | None = None, centrifugo=None) -> FastAPI:
    app = create_app(settings or Settings(), [api.router], base=None, on_startup=[_init])
    if centrifugo is not None:
        app.state.centrifugo = centrifugo
    return app


app = build_app()
