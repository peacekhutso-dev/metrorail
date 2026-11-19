import { useState } from "react";
import { useNavigate } from "react-router-dom";

import { api } from "../api/client";
import type { Ticket } from "../api/types";
import { useAsync } from "../api/useAsync";
import { useUserId } from "../auth/useSession";
import { Icon } from "../components/Icon";
import { Skeleton, stationName } from "../components/ui";

export function TripBar({ t }: { t: Ticket }) {
  if (!t.trips_total || t.trips_total <= 1) return null;
  return (
    <div className="trip-bar" aria-label={`${t.trips_used} of ${t.trips_total} trips used`}>
      {Array.from({ length: t.trips_total }, (_, i) => (
        <span key={i} className={i < t.trips_used - (t.trip_in_progress ? 1 : 0) ? "used" : i === t.trips_used - 1 && t.trip_in_progress ? "now" : ""} />
      ))}
    </div>
  );
}

export function ticketStatus(t: Ticket): { text: string; cls: string } {
  const until = t.trip_valid_until ? new Date(t.trip_valid_until).toLocaleTimeString("en-ZA", { hour: "2-digit", minute: "2-digit" }) : "";
  switch (t.status) {
    case "in_progress": return { text: t.trips_total ? `Trip ${t.trips_used} of ${t.trips_total} in progress · valid until ${until}` : `Journey in progress · valid until ${until}`, cls: "blue" };
    case "low": return { text: `${t.trips_left} trips left · Top up`, cls: "amber-soft" };
    case "used_up": return { text: "No trips left", cls: "grey" };
    case "expired": return { text: "Expired", cls: "grey" };
    default: return t.trips_total === null
      ? { text: `Unlimited · valid until ${new Date(t.valid_to).toLocaleDateString("en-ZA", { day: "numeric", month: "short" })}`, cls: "green" }
      : { text: `Ready to scan · ${t.trips_left} of ${t.trips_total} trips left`, cls: "green" };
  }
}

export function Tickets() {
  const nav = useNavigate();
  const user = useUserId();
  const [tab, setTab] = useState<"active" | "used">("active");
  const tickets = useAsync(() => (user ? api.tickets() : Promise.resolve([])), [user]);
  if (!user) {
    return (
      <div className="page"><h1>My tickets</h1>
        <div className="card center" style={{ display: "grid", gap: 12 }}><p>Log in to buy tickets and keep them on this phone.</p>
          <button className="btn primary" onClick={() => nav("/login")}>Log in</button></div></div>
    );
  }
  const list = (tickets.data ?? []).filter((t) => (tab === "active") === !["used_up", "expired"].includes(t.status));
  return (
    <div className="page">
      <div className="between"><h1>My tickets</h1><button className="chip on" onClick={() => nav("/buy")}><Icon name="plus" size={16} /> Buy ticket</button></div>
      <div className="seg">
        <button className={tab === "active" ? "on" : ""} onClick={() => setTab("active")}>Active</button>
        <button className={tab === "used" ? "on" : ""} onClick={() => setTab("used")}>Used and expired</button>
      </div>
      {tickets.loading && <Skeleton h={200} />}
      {!tickets.loading && list.length === 0 && <div className="card center muted">{tab === "active" ? "No tickets yet. Buy your first one." : "No used tickets yet."}</div>}
      {list.map((t) => {
        const s = ticketStatus(t);
        return (
          <div key={t.id} className="card" style={{ display: "grid", gap: 10, borderTop: "6px solid var(--blue)" }}>
            <span className="muted small">{t.product_name} · {t.id}</span>
            <h3>{stationName(t.from_station_id)} → {stationName(t.to_station_id)}</h3>
            <span className={`pill ${s.cls}`} style={{ justifySelf: "start" }}>{s.text}</span>
            <TripBar t={t} />
            <div className="row">
              {t.status !== "expired" && t.status !== "used_up" && <button className="btn secondary" onClick={() => nav(`/tickets/${t.id}`)}>Show QR</button>}
              {t.trips_total !== null && t.trips_total > 1 && <button className="btn secondary" onClick={() => nav(`/buy?topup=${t.id}`)}>Top up</button>}
            </div>
          </div>
        );
      })}
      <p className="muted small">Your tickets work offline. Open the QR before you reach the conductor.</p>
    </div>
  );
}
