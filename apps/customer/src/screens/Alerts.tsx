import { useEffect } from "react";
import { useNavigate } from "react-router-dom";

import { api } from "../api/client";
import { useAsync } from "../api/useAsync";
import { useUserId } from "../auth/useSession";
import { Icon } from "../components/Icon";
import { Skeleton } from "../components/ui";

const TONE: Record<string, string> = { stopped: "red", disruption: "red", held: "amber", delay: "amber" };

export function Alerts() {
  const nav = useNavigate();
  const user = useUserId();
  const alerts = useAsync(() => (user ? api.alerts() : Promise.resolve([])), [user], 30000);
  useEffect(() => { if (user) api.readAll().catch(() => {}); }, [user]);
  return (
    <div className="page">
      <h1>Alerts</h1>
      {!user && <div className="card" style={{ display: "grid", gap: 12 }}><p>Log in and save a trip to get alerts when your train is late, held or stopped.</p><button className="btn primary" onClick={() => nav("/login")}>Log in</button></div>}
      {alerts.loading && user && <Skeleton h={160} />}
      {user && alerts.data?.length === 0 && <div className="card center muted">No alerts. We'll tell you here when one of your trains runs late.</div>}
      {!!alerts.data?.length && (
        <div className="list">
          {alerts.data.map((a) => (
            <div key={a.id} className="list-row" style={{ alignItems: "flex-start" }}>
              <span className={`pill ${TONE[a.kind] ?? "blue"}`} style={{ padding: 6 }}><Icon name={TONE[a.kind] ? "warn" : "bell"} size={16} /></span>
              <span style={{ flex: 1 }}><b>{a.title}</b>{!a.read && <span className="pill blue" style={{ marginLeft: 6 }}>New</span>}<br />
                <span className="muted">{a.body}</span><br />
                <span className="muted small">{new Date(a.created_at).toLocaleTimeString("en-ZA", { hour: "2-digit", minute: "2-digit" })}</span></span>
            </div>
          ))}
        </div>
      )}
      <div className="card" style={{ display: "grid", gap: 6 }}>
        <h3>Get these by SMS and email</h3>
        <p className="muted">Never miss a delay, even with no data. From R20/month. (Coming soon)</p>
      </div>
    </div>
  );
}
