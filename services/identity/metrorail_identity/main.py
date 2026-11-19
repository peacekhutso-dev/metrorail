from fastapi import FastAPI

from metrorail_common.app import create_app
from metrorail_common.config import ServiceSettings

from .api import router
from .models import Base
from .senders import CodeSender
from .tokens import Signer


class Settings(ServiceSettings):
    service_name: str = "identity"
    signing_key: str | None = None  # IDENTITY_SIGNING_KEY: Ed25519 private key (PEM). Required in prod.


async def _init(app: FastAPI) -> None:
    st: Settings = app.state.settings
    if st.is_production and not st.signing_key:
        raise RuntimeError("IDENTITY_SIGNING_KEY is required in production")
    app.state.signer = Signer(st.signing_key, st.jwt_issuer, st.jwt_audience)
    app.state.sender = CodeSender()


def build_app(settings: Settings | None = None) -> FastAPI:
    return create_app(settings or Settings(), [router], base=Base, on_startup=[_init])


app = build_app()
