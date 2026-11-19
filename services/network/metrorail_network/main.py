from fastapi import FastAPI

from metrorail_common.app import create_app
from metrorail_common.config import ServiceSettings

from .api import router
from .models import Base
from .seed import seed_if_empty


class Settings(ServiceSettings):
    service_name: str = "network"


async def _seed(app: FastAPI) -> None:
    async with app.state.db.sessionmaker() as s:
        await seed_if_empty(s)


def build_app(settings: Settings | None = None) -> FastAPI:
    return create_app(settings or Settings(), [router], base=Base, on_startup=[_seed])


app = build_app()
