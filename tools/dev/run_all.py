"""Run every service locally WITHOUT Docker (SQLite files + a local NATS server).

    uv run python tools/dev/run_all.py          # services on :8101-:8109, gateway on :8080

A tiny built-in gateway forwards /api/<service>/... like Kong does, so the app and the
simulator work the same as with docker-compose. Needs `nats-server` on PATH
(https://github.com/nats-io/nats-server/releases) — or set EVENT_BUS=memory to skip it
(each service then only sees its own events).
"""

import asyncio
import os
import shutil
import signal
import subprocess
import sys
from pathlib import Path

import httpx
import uvicorn
from fastapi import FastAPI, Request, Response

ROOT = Path(__file__).resolve().parents[2]
DATA = ROOT / ".dev-data"
SERVICES = ["network", "tracking", "eta", "realtime", "identity", "trips", "notifications",
            "ticketing", "billing"]
PORTS = {name: 8101 + i for i, name in enumerate(SERVICES)}


def env_for(name: str) -> dict:
    e = dict(os.environ)
    e.update({
        "SERVICE_NAME": name, "ENVIRONMENT": "local",
        "EVENT_BUS": os.environ.get("EVENT_BUS", "nats"), "NATS_URL": "nats://127.0.0.1:4222",
        "DATABASE_URL": f"sqlite+aiosqlite:///{DATA / name}.db",
        "JWKS_URL": f"http://127.0.0.1:{PORTS['identity']}/.well-known/jwks.json",
        "CENTRIFUGO_API_URL": "http://127.0.0.1:8001", "CENTRIFUGO_PUBLIC_URL": "http://localhost:8001",
        **{f"{s.upper()}_URL": f"http://127.0.0.1:{p}" for s, p in PORTS.items()},
    })
    return e


gateway = FastAPI(title="dev gateway")
client = httpx.AsyncClient(timeout=10)


@gateway.api_route("/api/{service}/{path:path}", methods=["GET", "POST", "PUT", "DELETE"])
async def proxy(service: str, path: str, request: Request):
    if service not in PORTS:
        return Response(status_code=404)
    r = await client.request(
        request.method, f"http://127.0.0.1:{PORTS[service]}/{path}", params=request.query_params,
        content=await request.body(),
        headers={k: v for k, v in request.headers.items() if k.lower() not in ("host", "content-length")})
    return Response(r.content, r.status_code, {"content-type": r.headers.get("content-type", "")})


async def main() -> None:
    DATA.mkdir(exist_ok=True)
    procs = []
    if os.environ.get("EVENT_BUS", "nats") == "nats":
        nats = shutil.which("nats-server")
        if not nats:
            sys.exit("nats-server not found. Install it, or run with EVENT_BUS=memory")
        procs.append(subprocess.Popen(  # noqa: ASYNC220 - startup only
            [nats, "-js", "-p", "4222"], stdout=subprocess.DEVNULL))
        await asyncio.sleep(0.5)
    for name, port in PORTS.items():
        procs.append(subprocess.Popen(  # noqa: ASYNC220 - startup only
            [sys.executable, "-m", "uvicorn", f"metrorail_{name}.main:app", "--port", str(port),
             "--log-level", "warning"], env=env_for(name), cwd=ROOT))
    print("Services:", ", ".join(f"{n}:{p}" for n, p in PORTS.items()))
    print("Gateway:  http://localhost:8080/api/<service>/...")
    server = uvicorn.Server(uvicorn.Config(gateway, port=8080, log_level="warning"))
    try:
        await server.serve()
    finally:
        for p in procs:
            p.send_signal(signal.SIGINT)
        for p in procs:
            p.wait(timeout=10)


if __name__ == "__main__":
    asyncio.run(main())
