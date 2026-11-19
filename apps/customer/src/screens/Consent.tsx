import { useState } from "react";
import { useNavigate } from "react-router-dom";

import { api } from "../api/client";
import { Switch } from "../components/ui";

const ROWS = [
  ["trip_alerts", "Trip alerts", "Free in-app alerts when your saved trains are late or cancelled."],
  ["nearest_station", "Nearest station", "Use your location on this phone to find your nearest station. Never stored with your account."],
  ["usage_counts", "Anonymous usage counts", "Helps us see which stations are busiest. No names."],
] as const;

export function Consent() {
  const nav = useNavigate();
  const [c, setC] = useState({ trip_alerts: true, nearest_station: false, usage_counts: true });
  return (
    <div className="page no-tabs">
      <p className="muted">Step 3 of 3</p>
      <h1>You're in. Choose what we may use.</h1>
      <p style={{ color: "var(--body)" }}>You can change any of these later in Account. Turning something off never stops you tracking trains.</p>
      <div className="list">
        {ROWS.map(([k, t, d]) => (
          <div className="list-row" key={k}>
            <span style={{ flex: 1 }}><b>{t}</b><br /><span className="muted">{d}</span></span>
            <Switch on={c[k]} label={t} onChange={(v) => setC({ ...c, [k]: v })} />
          </div>
        ))}
      </div>
      <p className="muted">We never sell your data or show ads based on who you are.</p>
      <div className="spacer" />
      <button className="btn primary" onClick={async () => { await api.setConsents(c).catch(() => {}); nav("/", { replace: true }); }}>Continue</button>
    </div>
  );
}
