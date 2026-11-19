"""Settings shared by all services. Values come from environment variables (12-factor).

Each service reads variables with its own prefix, e.g. NETWORK_DATABASE_URL, plus shared ones
without a prefix (NATS_URL, JWT_PUBLIC_KEY ...). See infra/.env.example.
"""

from functools import lru_cache

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class ServiceSettings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    service_name: str = "service"
    environment: str = Field(default="local", description="local | test | staging | production")
    log_level: str = "INFO"

    # Postgres in docker-compose / production, SQLite for unit tests.
    database_url: str = "sqlite+aiosqlite:///:memory:"

    # Events: "memory" for tests and single-process dev, "nats" for docker-compose / prod.
    event_bus: str = "memory"
    nats_url: str = "nats://localhost:4222"

    # Auth: identity service signs access tokens with Ed25519 (EdDSA). Other services only need
    # the public key (PEM) or the JWKS URL to verify them.
    jwt_issuer: str = "metrorail-identity"
    jwt_audience: str = "metrorail"
    jwt_public_key: str | None = None
    jwks_url: str | None = None
    # Local/test only: lets you call protected endpoints with "Authorization: Bearer dev:<user_id>".
    allow_dev_tokens: bool = True

    cors_origins: list[str] = ["http://localhost:5173", "capacitor://localhost", "http://localhost"]

    @property
    def is_production(self) -> bool:
        return self.environment == "production"


@lru_cache
def base_settings() -> ServiceSettings:
    return ServiceSettings()
