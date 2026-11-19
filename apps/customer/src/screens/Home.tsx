import { useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";

import { api } from "../api/client";
import { useAsync } from "../api/useAsync";
import { useUserId } from "../auth/useSession";
import { Icon } from "../components/Icon";
import { TripCard } from "../components/TripCard";
import { Skeleton, StatusPill, stationName } from "../components/ui";
import { useLiveTrains } from "../realtime/useLive";

const DEFAULT_STATION = "ST-RONDEBOSCH";

export function Home() {
  return useUserId() ? <MyRoutes /> : <GuestHome />;
}

function MyRoutes() {
  const nav = useNavigate();
  const trips = useAsync(() => api.trips(), []);
  const [toast, setToast] = useState<string | null>(null);
  const trainNos = useMemo(() => (trips.data ?? []).map((t) => t.train_no).filter(Boolean) as string[], [trips.data]);
  const live = useLiveTrains(trainNos, (a) => { setToast(`${a.title}: ${a.body}`); setTimeout(() => setToast(null), 8000); });
  const today = new Date().toLocaleDateString("en-ZA", { weekday: "long", day: "numeric", month: "long" });
  const [next, ...later] = trips.data ?? [];

  return (
    <div className="page">
      <div className="between">
        <div><p className="muted">{today}</p><h1>My routes</h1></div>
        <button className="icon-btn" aria-label="Add a trip" onClick={() => nav("/trips/new")}><Icon name="plus" /></button>
      </div>
      <button className="card row" style={{ border: 0, color: "var(--muted)", fontWeight: 600 }} onClick={() => nav("/plan")}>
        <Icon name="search" /> Going somewhere else?
      </button>
      {toast && <div className="banner amber" role="status">{toast}</div>}
      {trips.loading && <Skeleton h={380} />}
      {!trips.loading && !next && (
        <div className="card center" style={{ display: "grid", gap: 12 }}>
          <p>Save the trip you take most and Home will show your next train first.</p>
          <button className="btn primary" onClick={() => nav("/trips/new")}>Add a trip</button>
        </div>
      )}
      {next?.train_no && <TripCard trip={next} live={live[next.train_no]} />}
      {later.map((t) => (
        <button key={t.id} className="card" style={{ border: 0, textAlign: "left" }} onClick={() => t.train_no && nav(`/live/${t.train_no}?board=${t.from_station_id}&alight=${t.to_station_id}`)}>
          <div className="between"><b>{t.name}</b>{t.train_no && <StatusPill state={live[t.train_no]?.state} delay={live[t.train_no]?.delay_min ?? 0} />}</div>
          <h3 style={{ marginTop: 6 }}>{stationName(t.from_station_id)} → {stationName(t.to_station_id)}</h3>
          <p className="muted">{t.days.length === 5 ? "Mon to Fri" : t.days.join(", ")} · usual train {t.usual_time}</p>
        </button>
      ))}
      <div className="row" style={{ justifyContent: "center", gap: 18 }}>
        <a onClick={() => nav("/trips")}>Manage my trips</a>
        <a onClick={() => nav("/trips/new")}>+ Add a trip</a>
      </div>
    </div>
  );
}

function GuestHome() {
  const nav = useNavigate();
  const deps = useAsync(() => api.departures(DEFAULT_STATION, undefined, 3), [], 30000);
  const live = useLiveTrains((deps.data ?? []).map((d) => d.train_no));
  return (
    <div className="page">
      <div className="between"><h1>Find your train</h1><button className="chip" onClick={() => nav("/login")}>Log in</button></div>
      <button className="card" style={{ border: 0, textAlign: "left", display: "grid", gap: 8 }} onClick={() => nav("/plan")}>
        <span className="muted small">From</span><b>{stationName(DEFAULT_STATION)} · nearest</b>
        <span style={{ height: 1, background: "var(--line)" }} />
        <span className="muted">Where to?</span>
      </button>
      <div className="between"><h3>Next trains from {stationName(DEFAULT_STATION)}</h3><a onClick={() => nav(`/departures/${DEFAULT_STATION}`)}>See all</a></div>
      <div className="list">
        {deps.loading && <Skeleton h={180} />}
        {deps.data?.map((d) => (
          <button key={d.train_no} className="list-row" onClick={() => nav(`/live/${d.train_no}`)}>
            <span className="pill blue" style={{ minWidth: 46, justifyContent: "center" }}>{d.train_no}</span>
            <span style={{ flex: 1 }}><b>to {d.terminus}</b><br /><span className="muted">{d.scheduled} · platform {d.platform}</span></span>
            <StatusPill state={live[d.train_no]?.state} delay={live[d.train_no]?.delay_min ?? 0} />
            <b style={{ minWidth: 48, textAlign: "right" }}>{Math.max(d.in_min, 0)} min</b>
          </button>
        ))}
      </div>
      <div className="card" style={{ background: "var(--dark)", color: "#fff", display: "grid", gap: 10 }}>
        <h3>Save the routes you take</h3>
        <p style={{ color: "var(--dark-muted)" }}>Home will show only your trains, with alerts when they run late.</p>
        <button className="btn amber" onClick={() => nav("/login")}>Log in</button>
      </div>
    </div>
  );
}
