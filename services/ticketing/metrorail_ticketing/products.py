"""Ticket products. Prices are SAMPLE values for the demo — real fares come from PRASA's
fare tables (zone/distance based) and will replace `price_cents` with a fare lookup."""

from dataclasses import dataclass
from datetime import datetime, timedelta

from metrorail_common.timeutil import SAST


@dataclass(frozen=True)
class Product:
    id: str
    name: str
    description: str
    price_cents: int
    trips: int | None  # None = unlimited


PRODUCTS = {
    "single": Product("single", "Single trip", "One journey today, changes included.", 1200, 1),
    "ten_trip": Product("ten_trip", "10-trip", "One trip per journey, not per scan. Valid 60 days.",
                        9500, 10),
    "weekly": Product("weekly", "Weekly", "Unlimited on this route for 7 days.", 11000, None),
    "monthly": Product("monthly", "Monthly", "Unlimited on this route until month end.", 42000, None),
}
TOPUPS = {10: 9500, 20: 18500}


def validity(product_id: str, start: datetime) -> tuple[datetime, datetime]:
    local = start.astimezone(SAST)
    day_start = local.replace(hour=0, minute=0, second=0, microsecond=0)
    if product_id == "single":
        return start, day_start + timedelta(days=1)
    if product_id == "ten_trip":
        return start, day_start + timedelta(days=61)
    if product_id == "weekly":
        return start, day_start + timedelta(days=7)
    nxt = (day_start.replace(day=28) + timedelta(days=4)).replace(day=1)
    return start, nxt  # monthly: until the end of this month


def journey_window_min(scheduled_min: int, changes: int = 0) -> int:
    """How long one 'trip' stays open after the first scan (covers changes and re-scans).
    scheduled journey x 1.5 + 30 min per change, between 90 min and 4 h."""
    return max(90, min(240, round(scheduled_min * 1.5 + 30 * changes)))
