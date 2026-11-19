"""What alert (if any) a train event produces for one saved trip. Pure, so copy is testable."""

from dataclasses import dataclass

STATION_NAMES: dict[str, str] = {}  # filled from network at startup; ids are a fine fallback


def name(station_id: str | None) -> str:
    if not station_id:
        return "the next station"
    return STATION_NAMES.get(station_id) or station_id.removeprefix("ST-").replace("-", " ").title()


@dataclass
class AlertDraft:
    kind: str
    title: str
    body: str


def for_train_event(ev: dict, trip_name: str, from_station_id: str, usual_time: str) -> AlertDraft | None:
    state, delay = ev.get("state"), int(ev.get("delay_min") or 0)
    where = f"your {usual_time} from {name(from_station_id)}"
    mins = ev.get("stationary_s", 0) // 60
    if state == "disruption":
        return AlertDraft("disruption", "Possible disruption on your line",
                          f"Train {ev['train_no']} ({trip_name}) hasn't moved for {mins} min near "
                          f"{name(ev.get('last_station_id'))}. Check other ways to travel.")
    if state == "stopped":
        return AlertDraft("stopped", "Your train has stopped",
                          f"Train {ev['train_no']} for {where} has stopped between "
                          f"{name(ev.get('last_station_id'))} and {name(ev.get('next_station_id'))}"
                          f" for {mins} min. Times will change.")
    if state == "held":
        return AlertDraft("held", "Your train is held",
                          f"Train {ev['train_no']} for {where} is held at "
                          f"{name(ev.get('at_station_id') or ev.get('last_station_id'))}.")
    if delay >= 2:
        return AlertDraft("delay", "Your train is delayed",
                          f"Train {ev['train_no']} for {where} is running {delay} min late.")
    return None
