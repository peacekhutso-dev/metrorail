import { useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";

import { api } from "../api/client";
import type { Station } from "../api/types";
import { useAsync } from "../api/useAsync";
import { useUserId } from "../auth/useSession";
import { Icon } from "../components/Icon";
import { ErrorNote, Skeleton } from "../components/ui";

/** Where to? + Journey results in one screen. */
export function Plan() {
  const nav = useNavigate();
  const user = useUserId();
  const [params, setParams] = useSearchParams();
  const from = params.get("from") ?? "ST-RONDEBOSCH";
  const to = params.get("to");
  const stations = useAsync(() => api.stations(), []);
  const [picking, setPicking] = useState<"from" | "to" | null>(to ? null : "to");
  const [q, setQ] = useState("");
  const results = useAsync(() => (to ? api.journeys(from, to) : Promise.resolve([])), [from, to]);
  const name = (id: string | null) => stations.data?.find((s) => s.id === id)?.name ?? "";

  function pick(s: Station) {
    const next = new URLSearchParams(params);
    next.set(picking!, s.id);
    setParams(next, { replace: true });
    setPicking(null);
    setQ("");
  }

  async function useLocation() {
    navigator.geolocation?.getCurrentPosition(async (p) => {
      const s = await api.nearest(p.coords.latitude, p.coords.longitude);
      const next = new URLSearchParams(params);
      next.set("from", s.id);
      setParams(next, { replace: true });
    });
  }

  return (
    <div className="page">
      <h1>Plan a trip</h1>
      <div className="card" style={{ display: "grid", gap: 8 }}>
        <button className="list-row" style={{ padding: 0, minHeight: 44 }} onClick={() => setPicking("from")}>
          <span style={{ flex: 1 }}><span className="muted small">From</span><br /><b>{name(from) || "Choose a station"}</b></span>
          <a onClick={(e) => { e.stopPropagation(); useLocation(); }} className="row small"><Icon name="pin" size={16} />Use my location</a>
        </button>
        <span style={{ height: 1, background: "var(--line)" }} />
        <button className="list-row" style={{ padding: 0, minHeight: 44 }} onClick={() => setPicking("to")}>
          <span><span className="muted small">To</span><br /><b>{name(to) || <span className="muted">Where to?</span>}</b></span>
        </button>
      </div>

      {picking && (
        <>
          <input className="input" autoFocus placeholder="Search stations" value={q} onChange={(e) => setQ(e.target.value)} aria-label="Search stations" />
          <div className="list">
            {(stations.data ?? []).filter((s) => s.name.toLowerCase().includes(q.toLowerCase())).slice(0, 12).map((s) => (
              <button key={s.id} className="list-row" onClick={() => pick(s)}>
                <span style={{ width: 10, height: 10, borderRadius: "50%", background: "var(--blue)" }} />
                <span style={{ flex: 1 }}><b>{s.name}</b><br /><span className="muted">Southern line</span></span>
                <Icon name="chevron" size={16} />
              </button>
            ))}
          </div>
        </>
      )}

      {!picking && to && (
        <>
          <p className="muted">Today · leaving now</p>
          <ErrorNote error={results.error} />
          {results.loading && <Skeleton h={120} />}
          {results.data?.length === 0 && <p className="muted">No more direct trains today.</p>}
          {results.data?.map((r) => (
            <button key={r.train_no} className="card" style={{ border: 0, textAlign: "left", display: "grid", gap: 8 }}
              onClick={() => nav(`/live/${r.train_no}?board=${from}&alight=${to}`)}>
              <div className="between"><h2>{r.departs} → {r.arrives}</h2><b>{r.duration_min} min</b></div>
              <div className="row"><span className="pill blue">Southern line</span><span className="muted">Direct · Train {r.train_no} · platform {r.platform}</span></div>
              <a className="row">Track live <Icon name="arrow" size={16} /></a>
            </button>
          ))}
          {user && <button className="btn secondary" onClick={() => nav(`/trips/new?from=${from}&to=${to}`)}>Save this trip for alerts</button>}
        </>
      )}
    </div>
  );
}
