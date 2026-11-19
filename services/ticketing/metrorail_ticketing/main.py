from fastapi import FastAPI

from metrorail_common.app import create_app
from metrorail_common.config import ServiceSettings

from . import api
from .crypto import Keys
from .models import Base


class Settings(ServiceSettings):
    service_name: str = "ticketing"
    issuer_key: str | None = None   # TICKETING_ISSUER_KEY  Ed25519 private PEM (required in prod)
    scanner_key: str | None = None  # TICKETING_SCANNER_KEY X25519 private PEM (shared with scanners)


async def _init(app: FastAPI) -> None:
    st: Settings = app.state.settings
    if st.is_production and not (st.issuer_key and st.scanner_key):
        raise RuntimeError("TICKETING_ISSUER_KEY and TICKETING_SCANNER_KEY are required in production")
    app.state.keys = Keys.load_or_generate(st.issuer_key, st.scanner_key)

    async def paid(_s, ev):
        await api.on_payment_succeeded(app, ev)

    async def failed(_s, ev):
        await api.on_payment_failed(app, ev)

    await app.state.bus.subscribe("billing.payment.succeeded", paid)
    await app.state.bus.subscribe("billing.payment.failed", failed)


def build_app(settings: Settings | None = None) -> FastAPI:
    return create_app(settings or Settings(), [api.router], base=Base, on_startup=[_init])


app = build_app()
