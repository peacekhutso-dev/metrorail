import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";

import { api } from "../api/client";
import { useAsync } from "../api/useAsync";
import { Skeleton, StatusPill, TopBar, stationName } from "../components/ui";
import { useLiveTrains } from "../realtime/useLive";

export function Departures() {
  const { stationId = "ST-RONDEBOSCH" } = useParams();
  const nav = useNavigate();
  const [dir, setDir] = useState<"northbound" | "southbound">("southbound");
  const deps = useAsync(() => api.departures(stationId, dir, 8), [stationId, dir], 30000);
  const live = useLiveTrains((deps.data ?? []).slice(0, 4).map((d) => d.train_no));
  return (
    <div className="page">
      <TopBar />
      <h1>{stationName(stationId)}</h1>
      <p className="muted">Southern line · Platforms 1 and 2</p>
      <div className="seg">
        <button className={dir === "northbound" ? "on" : ""} onClick={() => setDir("northbound")}>To Cape Town</button>
        <button className={dir === "southbound" ? "on" : ""} onClick={() => setDir("southbound")}>To Simon's Town</button>
      </div>
      <div className="list">
        {deps.loading && <Skeleton h={240} />}
        {deps.data?.map((d) => {
          const l = live[d.train_no];
          return (
            <button key={d.train_no} className="list-row" onClick={() => nav(`/live/${d.train_no}?board=${stationId}`)}>
              <b style={{ fontSize: 20, minWidth: 58 }}>{d.scheduled}</b>
              <span style={{ flex: 1 }}><b>{d.terminus}</b><br /><span className="muted">Train {d.train_no} · platform {d.platform}</span></span>
              <StatusPill state={l?.state} delay={l?.delay_min ?? 0} />
            </button>
          );
        })}
      </div>
      <p className="muted small">Live from tracked trains. Tap a train to follow it stop by stop.</p>
    </div>
  );
}
