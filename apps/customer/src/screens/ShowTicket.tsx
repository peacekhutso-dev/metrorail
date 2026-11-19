import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import QRCode from "qrcode";

import { api } from "../api/client";
import { useAsync } from "../api/useAsync";
import { Icon } from "../components/Icon";
import { stationName } from "../components/ui";
import { deviceKeyPair } from "../ticket/deviceKey";
import { SLOT_SECONDS, buildQr } from "../ticket/qr";
import { TripBar, ticketStatus } from "./Tickets";

/** Full-screen ticket. The QR silently changes every 30 s; screenshots are blocked (Android)
 *  or the QR is hidden while recording (iOS) via @capacitor-community/privacy-screen. */
export function ShowTicket() {
  const { ticketId = "" } = useParams();
  const nav = useNavigate();
  const t = useAsync(() => api.ticket(ticketId), [ticketId]);
  const keys = useAsync(() => api.ticketKeys(), []);
  const [svg, setSvg] = useState("");

  useEffect(() => {
    let on = true;
    import("@capacitor-community/privacy-screen").then(({ PrivacyScreen }) => PrivacyScreen.enable()).catch(() => {});
    return () => {
      on = false;
      import("@capacitor-community/privacy-screen").then(({ PrivacyScreen }) => PrivacyScreen.disable()).catch(() => {});
      void on;
    };
  }, []);

  useEffect(() => {
    if (!t.data || !keys.data) return;
    let stop = false;
    const draw = async () => {
      const { privateKey } = await deviceKeyPair();
      const qr = await buildQr({ cert: t.data!.cert, deviceKey: privateKey, scannerPubB64: keys.data!.scanner_x25519, tripHint: t.data!.trips_used });
      if (!stop) setSvg(await QRCode.toString(qr, { type: "svg", errorCorrectionLevel: "M", margin: 2 }));
    };
    draw();
    const timer = setInterval(draw, SLOT_SECONDS * 1000);
    return () => { stop = true; clearInterval(timer); };
  }, [t.data, keys.data]);

  if (!t.data) return <div className="ticket-screen" />;
  const s = ticketStatus(t.data);
  return (
    <div className="ticket-screen">
      <div className="between">
        <button className="icon-btn" style={{ background: "rgba(255,255,255,.16)", color: "#fff", boxShadow: "none" }} aria-label="Close" onClick={() => nav(-1)}><Icon name="close" /></button>
        <span className="glass">Brightness raised</span>
      </div>
      <span className="glass" style={{ alignSelf: "center" }}><Icon name="lock" size={14} /> Screenshots and screen recording are blocked</span>
      <div className="ticket">
        <div style={{ padding: "18px 18px 8px", display: "grid", gap: 8, justifyItems: "center", textAlign: "center" }}>
          <span className="muted small">{t.data.product_name} · Southern line</span>
          <h2>{stationName(t.data.from_station_id)} → {stationName(t.data.to_station_id)}</h2>
          <span className={`pill ${s.cls}`}>{s.text}</span>
          <TripBar t={t.data} />
          {t.data.trips_left !== null && t.data.trips_total! > 1 && <span className="muted small">{t.data.trips_left} trips left{t.data.trip_in_progress ? " after this one" : ""}</span>}
          <div role="img" aria-label="Ticket QR code for the conductor" style={{ width: 230, maxWidth: "100%" }} dangerouslySetInnerHTML={{ __html: svg }} />
        </div>
        <div className="tear" />
        <div style={{ padding: 16, display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10, fontSize: 13 }}>
          <span><span className="muted">Ticket</span><br /><b>{t.data.id}</b></span>
          <span><span className="muted">Valid until</span><br /><b>{new Date(t.data.valid_to).toLocaleDateString("en-ZA", { day: "numeric", month: "short" })}</b></span>
        </div>
      </div>
      <p className="small" style={{ display: "flex", gap: 8 }}><Icon name="wifiOff" size={18} />Works without signal. One trip covers your whole journey, including changes and more than one scan.</p>
      {t.data.status === "low" && <button className="btn amber" onClick={() => nav(`/buy?topup=${t.data!.id}`)}>Top up trips</button>}
    </div>
  );
}
