import { useNavigate } from "react-router-dom";

import { api } from "../api/client";
import { useAsync } from "../api/useAsync";
import { Icon } from "../components/Icon";
import { Switch } from "../components/ui";

export function Account() {
  const nav = useNavigate();
  const me = useAsync(() => api.me(), []);
  const m = me.data;
  async function download() {
    const data = await api.exportData();
    const a = document.createElement("a");
    a.href = URL.createObjectURL(new Blob([JSON.stringify(data, null, 2)], { type: "application/json" }));
    a.download = "metrorail-my-data.json";
    a.click();
  }
  async function remove() {
    if (!confirm("Delete your account and all your trips, alerts and saved cards? This can't be undone.")) return;
    await api.deleteAccount();
    await api.logout();
    nav("/", { replace: true });
  }
  return (
    <div className="page">
      <div className="card row" style={{ background: "var(--dark)", color: "#fff" }}>
        <Icon name="user" size={28} />
        <span style={{ flex: 1 }}><b>{m?.phone_masked ?? m?.email ?? "…"}</b><br /><span style={{ color: "var(--dark-muted)", fontSize: 12.5 }}>{m?.plan === "free" ? "Free plan" : m?.plan} · member since {m?.member_since}</span></span>
      </div>
      <h3>Travel</h3>
      <div className="list">
        <button className="list-row" onClick={() => nav("/trips")}><span style={{ flex: 1 }}>My trips</span><Icon name="chevron" size={16} /></button>
        <button className="list-row" onClick={() => nav("/tickets")}><span style={{ flex: 1 }}>Tickets and receipts</span><Icon name="chevron" size={16} /></button>
      </div>
      <h3>Privacy</h3>
      {m && (
        <div className="list">
          {([["trip_alerts", "Trip alerts"], ["nearest_station", "Use my location for nearest station"], ["usage_counts", "Anonymous usage counts"]] as const).map(([k, label]) => (
            <div key={k} className="list-row"><span style={{ flex: 1 }}>{label}</span>
              <Switch on={m.consents[k]} label={label} onChange={async (v) => me.setData(await api.setConsents({ ...m.consents, [k]: v }))} /></div>
          ))}
          <button className="list-row" onClick={download}><span style={{ flex: 1 }}>Download my data</span><Icon name="chevron" size={16} /></button>
          <button className="list-row" style={{ color: "#a32a16" }} onClick={remove}><span style={{ flex: 1 }}>Delete my account</span><Icon name="chevron" size={16} /></button>
        </div>
      )}
      <button className="btn secondary" onClick={async () => { await api.logout(); nav("/", { replace: true }); }}>Log out</button>
    </div>
  );
}
