import { useNavigate } from "react-router-dom";

import { api } from "../api/client";
import type { SavedTrip, TrainLive } from "../api/types";
import { useAsync } from "../api/useAsync";
import { Icon } from "./Icon";
import { StopStrip } from "./StopStrip";
import { Skeleton, StatusPill, stationName } from "./ui";

const mmss = (s: number) => `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`;

/** The Home hero card: when your train reaches you, where it is, when you arrive. */
export function TripCard({ trip, live }: { trip: SavedTrip; live?: TrainLive }) {
  const nav = useNavigate();
  const train = trip.train_no!;
  const j = useAsync(() => api.journeyEta(train, trip.from_station_id, trip.to_station_id), [train, live?.state, live?.next_station_id], 30000);
  const eta = useAsync(() => api.trainEta(train), [train, live?.state, live?.next_station_id], 30000);
  if (!j.data || !eta.data) return <Skeleton h={380} />;

  const d = j.data;
  const state = live?.state ?? d.state;
  const stationary = live?.stationary_s ?? d.stationary_s;
  const delay = Math.max(0, minutes(d.board_predicted) - minutes(d.board_scheduled));
  const from = stationName(trip.from_station_id), to = stationName(trip.to_station_id);
  const last = stationName(live?.last_station_id), next = stationName(live?.next_station_id);

  let title = d.stops_until_board > 0 ? "Train is approaching" : "Train is at your station";
  let sub = d.stops_until_board > 0 ? `${d.stops_until_board} stop${d.stops_until_board > 1 ? "s" : ""} until ${from}` : "Board now";
  let titleColor = "#fff";
  if (state === "held") {
    title = `Train is held at ${stationName(live?.at_station_id) || last}`;
    sub = `Not moving for ${mmss(stationary)}. Holds here usually clear in about 4 min.`;
    titleColor = "var(--amber)";
  } else if (state === "stopped" || state === "disruption") {
    title = `Train stopped between ${last} and ${next}`;
    sub = `Not moving for ${mmss(stationary)}. Similar stops here took 8–15 min.`;
    titleColor = "var(--red-on-dark)";
  }

  return (
    <>
      <section className={`trip-card ${state ?? ""}`}>
        <div className="pad">
          <div className="between">
            <span className="row" style={{ gap: 8, fontWeight: 700, fontSize: 14 }}>
              <span style={{ width: 9, height: 9, borderRadius: "50%", background: "var(--blue-on-dark)" }} />
              {trip.name}
            </span>
            <StatusPill state={state} delay={delay} />
          </div>
          <h2 style={{ marginTop: 10, fontSize: 18 }}>{from} → {to}</h2>
          <p className="dm" style={{ marginTop: 4 }}>Train {train} · Southern Line</p>
        </div>
        <div className="rule" />
        <div className="pad" style={{ paddingTop: 12 }}>
          <p className="dm">Your train reaches {from} at</p>
          <div className="eta">{d.board_predicted}</div>
          <p className="dm">In {d.board_in_min} minutes · Scheduled {d.board_scheduled}</p>
        </div>
        <div className="rule" />
        <div className="pad" style={{ paddingTop: 12 }}>
          <h3 style={{ color: titleColor }}>{title}</h3>
          <p className="dm" style={{ marginTop: 3 }}>{sub}</p>
        </div>
        <StopStrip stops={eta.data.stops} board={trip.from_station_id} alight={trip.to_station_id}
          nextId={live?.next_station_id ?? null} state={state} />
        <div className="arrive">
          <span><span className="dm">You arrive at {to} at</span><br />
            <span className="dm" style={{ color: "#8E99A6" }}>{d.ride_min} min ride</span></span>
          <b>{d.alight_predicted}</b>
        </div>
        <button className="foot" onClick={() => nav(`/live/${train}?board=${trip.from_station_id}&alight=${trip.to_station_id}`)}>
          <span className="dm">Live train tracking</span>
          <b className="row" style={{ gap: 4 }}>Track live <Icon name="arrow" size={16} /></b>
        </button>
      </section>
      {(state === "stopped" || state === "disruption") && (
        <button className="banner red" style={{ border: 0, textAlign: "left" }}
          onClick={() => nav(`/plan?from=${trip.from_station_id}&to=${trip.to_station_id}`)}>
          <Icon name="warn" size={20} />
          <span style={{ flex: 1 }}>Your train isn't moving. See other ways to {to}</span>
          <Icon name="chevron" size={16} />
        </button>
      )}
    </>
  );
}

function minutes(hhmm: string) {
  return Number(hhmm.slice(0, 2)) * 60 + Number(hhmm.slice(3));
}
