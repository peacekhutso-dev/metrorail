"""Domain events between services (NATS JetStream in production).

Subjects follow `<domain>.<entity>.<event>`, e.g. `tracking.train.position`,
`tracking.train.state_changed`, `billing.payment.succeeded`, `ticketing.ticket.issued`.
Payload schemas live in contracts/asyncapi/metrorail-events.yaml.
"""

import asyncio
import json
from collections import defaultdict
from collections.abc import Awaitable, Callable
from typing import Any, Protocol

Handler = Callable[[str, dict[str, Any]], Awaitable[None]]


class EventBus(Protocol):
    async def connect(self) -> None: ...
    async def close(self) -> None: ...
    async def publish(self, subject: str, payload: dict[str, Any]) -> None: ...
    async def subscribe(self, subject: str, handler: Handler, queue: str | None = None) -> None: ...


def _matches(pattern: str, subject: str) -> bool:
    """NATS-style wildcards: '*' = one token, '>' = rest."""
    p, s = pattern.split("."), subject.split(".")
    for i, tok in enumerate(p):
        if tok == ">":
            return True
        if i >= len(s) or (tok != "*" and tok != s[i]):
            return False
    return len(p) == len(s)


class InMemoryBus:
    """Process-local bus for tests and single-process dev. Delivers synchronously (awaited)."""

    def __init__(self) -> None:
        self.handlers: dict[str, list[Handler]] = defaultdict(list)
        self.published: list[tuple[str, dict[str, Any]]] = []

    async def connect(self) -> None:
        return None

    async def close(self) -> None:
        return None

    async def publish(self, subject: str, payload: dict[str, Any]) -> None:
        self.published.append((subject, payload))
        for pattern, hs in list(self.handlers.items()):
            if _matches(pattern, subject):
                for h in hs:
                    await h(subject, payload)

    async def subscribe(self, subject: str, handler: Handler, queue: str | None = None) -> None:
        self.handlers[subject].append(handler)


class NatsBus:
    def __init__(self, url: str, service_name: str) -> None:
        self.url = url
        self.service_name = service_name
        self._nc = None

    async def connect(self) -> None:
        import nats  # imported lazily so tests don't need a NATS server

        for attempt in range(10):
            try:
                self._nc = await nats.connect(self.url, name=self.service_name)
                return
            except Exception:  # noqa: BLE001 - retry while NATS boots in compose
                await asyncio.sleep(min(2**attempt * 0.2, 3))
        raise RuntimeError(f"Could not connect to NATS at {self.url}")

    async def close(self) -> None:
        if self._nc:
            await self._nc.drain()

    async def publish(self, subject: str, payload: dict[str, Any]) -> None:
        assert self._nc, "NatsBus.connect() not called"
        await self._nc.publish(subject, json.dumps(payload, default=str).encode())

    async def subscribe(self, subject: str, handler: Handler, queue: str | None = None) -> None:
        assert self._nc, "NatsBus.connect() not called"

        async def _cb(msg):
            await handler(msg.subject, json.loads(msg.data))

        await self._nc.subscribe(subject, queue=queue or self.service_name, cb=_cb)


def make_bus(kind: str, url: str, service_name: str) -> EventBus:
    return NatsBus(url, service_name) if kind == "nats" else InMemoryBus()
