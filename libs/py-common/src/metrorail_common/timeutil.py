"""Times: store UTC, display Africa/Johannesburg (SAST, UTC+2, no DST).

DEMO_TIME=07:14 makes every service (and the simulator) behave as if it is 07:14 SAST today
when the process started, and then lets the clock run normally. Useful for pitching the
morning peak at any hour. Never set it in production.
"""

import os
import time
from datetime import UTC, datetime, timedelta, timezone

SAST = timezone(timedelta(hours=2), "SAST")

_DEMO = os.environ.get("DEMO_TIME")
_START = time.monotonic()
_BASE: datetime | None = None
if _DEMO:
    _h, _m = (int(x) for x in _DEMO.split(":"))
    _BASE = datetime.now(SAST).replace(hour=_h, minute=_m, second=0, microsecond=0).astimezone(UTC)


def now_utc() -> datetime:
    if _BASE is not None:
        return _BASE + timedelta(seconds=time.monotonic() - _START)
    return datetime.now(UTC)


def hhmm(dt: datetime) -> str:
    return dt.astimezone(SAST).strftime("%H:%M")
