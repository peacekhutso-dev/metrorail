import { useEffect, useState } from "react";
import { useNavigate, useParams, useSearchParams } from "react-router-dom";

import { ApiError, api } from "../api/client";
import type { TripInput } from "../api/types";
import { useAsync } from "../api/useAsync";
import { Icon } from "../components/Icon";
import { Switch, TopBar, stationName } from "../components/ui";

const DAYS = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

export function MyTrips() {
  const nav = useNavigate();
  const trips = useAsync(() => api.trips(), []);
  return (
    <div className="page">
      <TopBar title="My trips" right={<button className="chip on" onClick={() => nav("/trips/new")}><Icon name="plus" size={16} /> Add a trip</button>} />
      <p className="muted">Trips you take often. They show on Home with live status, and you get alerts when the train is late.</p>
      {trips.data?.length === 0 && <div className="card center muted">No saved trips yet.</div>}
      {trips.data?.map((t) => (
        <div key={t.id} className="card" style={{ display: "grid", gap: 8 }}>
          <div className="between"><span className="pill blue">{t.name}</span><span className="muted">{t.alert_delays ? "Alerts on" : "Alerts off"}</span></div>
          <h3>{stationName(t.from_station_id)} → {stationName(t.to_station_id)}</h3>
          <p className="muted">{t.days.join(", ")} · {t.usual_time} train{t.leave_reminder ? ` · leave-now reminder ${t.leave_minutes} min before` : ""}</p>
          <div className="row">
            <button className="btn secondary" onClick={() => nav(`/trips/${t.id}`)}>Edit</button>
            <button className="btn secondary" style={{ color: "#a32a16" }} onClick={async () => { await api.deleteTrip(t.id); trips.reload(); }}>Delete</button>
          </div>
        </div>
      ))}
      <button className="btn secondary" onClick={() => nav("/")}><Icon name="home" size={18} /> Back to Home</button>
    </div>
  );
}

export function EditTrip() {
  const nav = useNavigate();
  const { id } = useParams();
  const [p] = useSearchParams();
  const editing = id && id !== "new";
  const [t, setT] = useState<TripInput>({
    name: "Work", from_station_id: p.get("from") ?? "ST-RONDEBOSCH", to_station_id: p.get("to") ?? "ST-CLAREMONT",
    days: ["Mon", "Tue", "Wed", "Thu", "Fri"], usual_time: "07:18", train_no: p.get("train"),
    alert_delays: true, leave_reminder: true, leave_minutes: 10,
  });
  const [err, setErr] = useState("");
  const stations = useAsync(() => api.stations(), []);
  const options = useAsync(() => api.journeys(t.from_station_id, t.to_station_id), [t.from_station_id, t.to_station_id]);

  useEffect(() => {
    if (editing) api.trips().then((all) => { const f = all.find((x) => x.id === id); if (f) setT(f); });
  }, [editing, id]);

  const set = (patch: Partial<TripInput>) => setT({ ...t, ...patch });
  const trains = (options.data ?? []).slice(0, 4);  // only trains that go from -> to

  async function save() {
    try {
      if (editing) await api.editTrip(id!, t); else await api.addTrip(t);
      nav("/trips", { replace: true });
    } catch (e) { setErr((e as ApiError).message); }
  }

  return (
    <div className="page no-tabs">
      <TopBar close title={editing ? "Edit trip" : "Add a trip"} />
      <div className="field">
        <label htmlFor="name">Trip name</label>
        <input id="name" className="input" maxLength={30} value={t.name} onChange={(e) => set({ name: e.target.value })} placeholder="e.g. Work, Varsity, Gran's place" />
        <div className="chips">{["Work", "Home", "Varsity", "Gym"].map((n) => <button key={n} className={`chip ${t.name === n ? "on" : ""}`} onClick={() => set({ name: n })}>{n}</button>)}</div>
      </div>
      <div className="card" style={{ display: "grid", gap: 10 }}>
        {(["from_station_id", "to_station_id"] as const).map((k) => (
          <div className="field" key={k}>
            <label htmlFor={k}>{k === "from_station_id" ? "From" : "To"}</label>
            <select id={k} className="input" value={t[k]} onChange={(e) => set({ [k]: e.target.value } as Partial<TripInput>)}>
              {stations.data?.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
            </select>
          </div>
        ))}
      </div>
      <div className="field"><label>Days</label>
        <div style={{ display: "grid", gridTemplateColumns: "repeat(7, 1fr)", gap: 6 }}>
          {DAYS.map((d) => (
            <button key={d} aria-pressed={t.days.includes(d)} aria-label={d} className={`chip ${t.days.includes(d) ? "on" : ""}`} style={{ padding: 0 }}
              onClick={() => set({ days: t.days.includes(d) ? t.days.filter((x) => x !== d) : [...t.days, d] })}>{d[0]}</button>
          ))}
        </div>
      </div>
      <div className="field"><label>Your usual train</label>
        <div className="chips">{trains.map((d) => (
          <button key={d.train_no} className={`chip ${t.train_no === d.train_no ? "on" : ""}`}
            onClick={() => set({ train_no: d.train_no, usual_time: d.departs })}>{d.departs}</button>
        ))}</div>
      </div>
      <div className="list">
        <div className="list-row"><span style={{ flex: 1 }}><b>Alert me about delays</b><br /><span className="muted">and cancellations on this trip</span></span>
          <Switch on={t.alert_delays} label="Alert me about delays" onChange={(v) => set({ alert_delays: v })} /></div>
        <div className="list-row"><span style={{ flex: 1 }}><b>Leave-now reminder</b><br /><span className="muted">{t.leave_minutes} minutes before the train</span></span>
          <Switch on={t.leave_reminder} label="Leave-now reminder" onChange={(v) => set({ leave_reminder: v })} /></div>
      </div>
      {err && <p className="error-text">{err}</p>}
      <button className="btn primary" onClick={save} disabled={!t.name.trim() || !t.days.length}>{editing ? "Save changes" : "Add trip"}</button>
      <p className="muted center small">{t.name || "Untitled trip"}: {stationName(t.from_station_id)} → {stationName(t.to_station_id)}, {t.days.join(", ")}, {t.usual_time} train</p>
    </div>
  );
}
