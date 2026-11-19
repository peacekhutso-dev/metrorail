from fastapi import FastAPI

from metrorail_common.app import create_app
from metrorail_common.config import ServiceSettings

from . import api
from .models import Base
from .payat import MockPayAt, PayAtClient


class Settings(ServiceSettings):
    service_name: str = "billing"
    payat_mode: str = "mock"  # mock | sandbox | live
    payat_base_url: str = ""
    payat_merchant_id: str = ""
    payat_api_key: str = ""
    payat_webhook_secret: str = "mock-secret"


async def _init(app: FastAPI) -> None:
    st: Settings = app.state.settings
    if st.payat_mode == "mock":
        if st.is_production:
            raise RuntimeError("PAYAT_MODE=mock is not allowed in production")
        app.state.gateway = MockPayAt(st.payat_webhook_secret)
    else:
        app.state.gateway = PayAtClient(st.payat_base_url, st.payat_merchant_id, st.payat_api_key,
                                        st.payat_webhook_secret)

    async def purchase(_s, ev):
        await api.on_purchase_created(app, ev)

    async def deleted(_s, ev):
        await api.on_user_deleted(app, ev)

    await app.state.bus.subscribe("ticketing.purchase.created", purchase)
    await app.state.bus.subscribe("identity.user.deleted", deleted)


def build_app(settings: Settings | None = None) -> FastAPI:
    return create_app(settings or Settings(), [api.router], base=Base, on_startup=[_init])


app = build_app()
