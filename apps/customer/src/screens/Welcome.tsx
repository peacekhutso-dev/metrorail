import { useNavigate } from "react-router-dom";

import { Icon } from "../components/Icon";

export function Welcome() {
  const nav = useNavigate();
  const seen = () => { try { localStorage.setItem("metrorail.welcomed", "1"); } catch { /* ignore */ } };
  return (
    <div className="page no-tabs" style={{ gap: 18 }}>
      <div className="center" style={{ paddingTop: 12 }}>
        {/* Replace with the official Metrorail logo for the pitch (assets/metrorail-logo.png). */}
        <div style={{ width: 112, height: 80, margin: "0 auto", borderRadius: 12, background: "#fff", display: "grid", placeItems: "center", boxShadow: "var(--shadow)", fontWeight: 800, color: "var(--blue)" }}>MetroRail</div>
        <p className="muted small" style={{ marginTop: 6 }}>Concept for PRASA · pitch only</p>
      </div>
      <div className="card" style={{ background: "var(--blue)", color: "#fff", height: 200, display: "grid", placeItems: "center", fontWeight: 800 }}>
        <span className="pill" style={{ background: "#fff", color: "var(--blue-dark)" }}>Claremont in 14 min</span>
      </div>
      <h1>Know exactly where your train is</h1>
      <p style={{ color: "var(--body)" }}>Live trains on the map, an ETA at every stop, alerts when your train is late, and your ticket on your phone.</p>
      <div className="row"><Icon name="pin" /> Track any train live, no account needed</div>
      <div className="row"><Icon name="ticket" /> Buy tickets and pay by card in the app</div>
      <div className="spacer" />
      <button className="btn primary" onClick={() => { seen(); nav("/login"); }}>Log in or sign up</button>
      <button className="btn secondary" onClick={() => { seen(); nav("/"); }}>Continue as guest</button>
    </div>
  );
}
