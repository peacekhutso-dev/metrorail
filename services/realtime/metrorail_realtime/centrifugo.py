"""Centrifugo server API client (v5). Channels:
    trains:<train_no>        public live position/state for one train
    alerts:#<user_id>        private alerts for one user ('#' = user-limited channel)
"""

import httpx


class Centrifugo:
    def __init__(self, api_url: str, api_key: str):
        self.http = httpx.AsyncClient(base_url=api_url, timeout=3,
                                      headers={"X-API-Key": api_key})

    async def publish(self, channel: str, data: dict) -> None:
        r = await self.http.post("/api/publish", json={"channel": channel, "data": data})
        r.raise_for_status()

    async def close(self) -> None:
        await self.http.aclose()


class FakeCentrifugo:
    def __init__(self) -> None:
        self.sent: list[tuple[str, dict]] = []

    async def publish(self, channel: str, data: dict) -> None:
        self.sent.append((channel, data))

    async def close(self) -> None:
        return None
