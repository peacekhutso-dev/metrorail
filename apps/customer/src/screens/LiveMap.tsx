import { useNavigate, useParams, useSearchParams } from "react-router-dom";

import { api } from "../api/client";
import { useAsync } from "../api/useAsync";
import { useUserId } from "../auth/useSession";
import { Icon } from "../components/Icon";
import { Skeleton, StatusPill, TopBar } from "../components/ui";
import { useLiveTrains } from "../realtime/useLive";

/** Live route: schematic map of only this train's line + every stop with its predicted time.
 *  (Swap the schematic for MapLibre + OSM tiles when we add the full map.) */
export function LiveMap() {
  const { trainNo = "" } = useParams();
  const [p] = useSearchParams();
  const nav = useNavigate();
  const user = useUserId();
  const board = p.get("board"), alight = p.get("alight");
  const live = useLiveTrains([trainNo])[trainNo];
  const eta = useAsync(() => api.trainEta(trainNo), [trainNo, live?.state, live?.next_station_id], 20000);
  const stations = useAsync(() => api.stations(), []);
  if (!eta.data || !stations.data) return <div className="page"><TopBar /><Skeleton h={300} /><Skeleton h={300} /></div>;

  const coords = Object.fromEntries(stations.data.map((s) => [s.id, s]));
  const pts = eta.data.stops.map((s) => coords[s.station_id]).filter(Boolean);
  const lats = pts.map((s) => s.lat), lngs = pts.map((s) => s.lng);
  const [minLat, maxLat, minLng, maxLng] = [Math.min(...lats), Math.max(...lats), Math.min(...lngs), Math.max(...lngs)];
  const X = (lng: number) => 40 + ((lng - minLng) / (maxLng - minLng || 1)) * 280;
  const Y = (lat: number) => 20 + ((maxLat - lat) / (maxLat - minLat || 1)) * 340;
  const stuck = live?.state === "stopped" || live?.state === "disruption";
  const boardStop = eta.data.stops.find((s) => s.station_id === board);
  const delay = boardStop ? boardStop.delay_min : live?.delay_min ?? 0;

  return (
    <div className="page">
      <TopBar title={`Train ${trainNo}`} right={<StatusPill state={live?.state} delay={delay} />} />
      <div className="card" style={{ padding: 0, overflow: "hidden", background: "#EDF1EE" }}>
        <svg viewBox="0 0 360 380" width="100%" role="img" aria-label={`Map of train ${trainNo}'s route`}>
          <polyline fill="none" stroke="var(--blue)" strokeWidth={5} strokeLinecap="round" strokeLinejoin="round"
            points={pts.map((s) => `${X(s.lng)},${Y(s.lat)}`).join(" ")} />
          {pts.map((s) => (
            <g key={s.id}>
              <circle cx={X(s.lng)} cy={Y(s.lat)} r={s.id === board || s.id === alight ? 6 : 3.5} fill="#fff" stroke="var(--ink)" strokeWidth={1.5} />
              {(s.id === board || s.id === alight) && (
                <text x={X(s.lng) - 10} y={Y(s.lat) + 4} textAnchor="end" fontSize={11} fontWeight={800}>{s.id === board ? "Your stop" : "Get off"}</text>
              )}
            </g>
          ))}
          {live && (
            <g transform={`translate(${X(live.lng)} ${Y(live.lat)})`}>
              <rect x={-24} y={-12} width={48} height={24} rx={12} fill={stuck ? "var(--red)" : live.state === "held" ? "var(--amber)" : "var(--blue)"} stroke="#fff" strokeWidth={2} />
              <text y={4} textAnchor="middle" fontSize={11} fontWeight={800} fill="#fff">{trainNo}</text>
            </g>
          )}
        </svg>
      </div>
      {boardStop && (
        <div className="card">
          <p className="muted">Your train reaches {boardStop.station_name} at</p>
          <div className="between"><h1 style={{ fontSize: 34 }}>{boardStop.predicted}</h1>
            <span className="muted">likely {boardStop.likely_from}–{boardStop.likely_to}</span></div>
          {stuck && <p className="error-text" style={{ marginTop: 6 }}>Not moving for {Math.floor((live?.stationary_s ?? 0) / 60)} min. Times will change.</p>}
        </div>
      )}
      <div className="list">
        {eta.data.stops
          .slice(0, alight ? eta.data.stops.findIndex((s) => s.station_id === alight) + 1 || undefined : undefined)
          .filter((s) => !s.passed || s.station_id === live?.last_station_id).map((s) => (
          <div key={s.station_id} className="list-row" style={{ opacity: s.passed ? 0.5 : 1 }}>
            <span style={{ flex: 1 }}>
              <b>{s.station_name}</b>{s.station_id === live?.next_station_id && <span className="pill blue" style={{ marginLeft: 8 }}>Next</span>}
              {s.station_id === board && <span className="pill grey" style={{ marginLeft: 8 }}>Board here</span>}
              {s.station_id === alight && <span className="pill grey" style={{ marginLeft: 8 }}>Get off</span>}
            </span>
            <span className="muted" style={{ textDecoration: s.delay_min > 0 ? "line-through" : "none" }}>{s.delay_min > 0 ? s.scheduled : ""}</span>
            <b>{s.predicted}</b>
          </div>
        ))}
      </div>
      <div className="row">
        {user && board && alight && <button className="btn secondary" onClick={() => nav(`/trips/new?from=${board}&to=${alight}&train=${trainNo}`)}>Save trip</button>}
        <button className="btn secondary" onClick={() => board && api.report(trainNo, board, "delayed").then(() => alert("Thanks for reporting"))}>
          <Icon name="warn" size={18} /> Report</button>
      </div>
    </div>
  );
}
