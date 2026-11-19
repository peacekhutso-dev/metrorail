"""Tiny HTTP client for service-to-service calls (through the internal network, not Kong).

Base URLs come from env: NETWORK_URL, TRACKING_URL, ETA_URL, TICKETING_URL ...
Tests replace clients on app.state with fakes.
"""

import os
from typing import Any

import httpx

from .errors import AppError

DEFAULTS = {
    "network": "http://network:8000",
    "tracking": "http://tracking:8000",
    "eta": "http://eta:8000",
    "trips": "http://trips:8000",
    "ticketing": "http://ticketing:8000",
    "billing": "http://billing:8000",
    "identity": "http://identity:8000",
}


def service_url(name: str) -> str:
    return os.environ.get(f"{name.upper()}_URL", DEFAULTS[name])


class ServiceClient:
    def __init__(self, name: str, base_url: str | None = None, timeout: float = 3.0):
        self.name = name
        self._http = httpx.AsyncClient(base_url=base_url or service_url(name), timeout=timeout)

    async def get(self, path: str, **params: Any) -> Any:
        r = await self._http.get(path, params={k: v for k, v in params.items() if v is not None})
        return self._json(r)

    async def post(self, path: str, json: Any = None) -> Any:
        return self._json(await self._http.post(path, json=json))

    def _json(self, r: httpx.Response) -> Any:
        if r.status_code >= 400:
            try:
                err = r.json()["error"]
                raise AppError(err["message"], code=err["code"], status_code=r.status_code)
            except (ValueError, KeyError):
                raise AppError(f"{self.name} returned {r.status_code}", code="upstream_error",
                               status_code=502) from None
        return r.json()

    async def close(self) -> None:
        await self._http.aclose()
