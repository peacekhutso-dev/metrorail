from datetime import UTC, datetime, timedelta

import jwt
from fastapi import APIRouter, Depends, FastAPI, Query, Request

from metrorail_common.auth import Principal, optional_user
from metrorail_common.errors import AppError
from metrorail_common.logging import get_logger

router = APIRouter(tags=["realtime"])
log = get_logger("realtime")
MAX_TRAINS = 5


@router.get("/token")
async def connection_token(
    request: Request,
    trains: str = Query("", description="comma-separated train numbers to follow, e.g. 3412,3420"),
    user: Principal | None = Depends(optional_user),
):
    """Token for Centrifugo's SSE endpoint. Subscriptions are decided here (server-side), so the
    phone only needs a plain EventSource: GET /connection/uni_sse?cf_connect={"token": ...}"""
    nums = [t for t in trains.split(",") if t][:MAX_TRAINS]
    if any(not t.isdigit() for t in nums):
        raise AppError("Train numbers only", code="invalid_trains")
    channels = [f"trains:{t}" for t in nums]
    if user:
        channels.append(f"alerts:#{user.user_id}")
    st = request.app.state.settings
    claims = {"sub": user.user_id if user else "", "channels": channels,
              "exp": int((datetime.now(UTC) + timedelta(hours=1)).timestamp())}
    return {"token": jwt.encode(claims, st.centrifugo_token_secret, algorithm="HS256"),
            "sse_url": st.centrifugo_public_url + "/connection/uni_sse", "channels": channels}


def _train_view(ev: dict) -> dict:
    keep = ("train_no", "lat", "lng", "state", "stationary_s", "last_station_id",
            "next_station_id", "at_station_id", "delay_min", "seen_at")
    return {k: ev.get(k) for k in keep}


async def forward(app: FastAPI, subject: str, ev: dict) -> None:
    try:
        await _forward(app, subject, ev)
    except Exception as e:  # noqa: BLE001 - live updates are best-effort; never crash the consumer
        log.warning("centrifugo_publish_failed", subject=subject, error=str(e))


async def _forward(app: FastAPI, subject: str, ev: dict) -> None:
    c = app.state.centrifugo
    if subject.startswith("tracking.train."):
        await c.publish(f"trains:{ev['train_no']}", {"type": subject.rsplit(".", 1)[1],
                                                     **_train_view(ev)})
    elif subject == "notifications.alert.created":
        await c.publish(f"alerts:#{ev['user_id']}", {"type": "alert", **{
            k: ev[k] for k in ("id", "kind", "title", "body", "train_no")}})
