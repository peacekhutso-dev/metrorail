import { useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";

import { ApiError, api } from "../api/client";
import type { Payment, Purchase, SavedCard } from "../api/types";
import { useAsync } from "../api/useAsync";
import { Icon } from "../components/Icon";
import { ErrorNote, TopBar, rand, stationName } from "../components/ui";
import { devicePublicKeyB64 } from "../ticket/deviceKey";

/** Buy a ticket (or top up) -> checkout -> Pay@ card form -> bank approval -> ticket. */
export function Buy() {
  const nav = useNavigate();
  const [p] = useSearchParams();
  const topup = p.get("topup");
  const products = useAsync(() => api.products(), []);
  const cards = useAsync(() => api.cards(), []);
  const [product, setProduct] = useState("single");
  const [trips, setTrips] = useState(10);
  const [from] = useState(p.get("from") ?? "ST-RONDEBOSCH");
  const [to] = useState(p.get("to") ?? "ST-CLAREMONT");
  const [purchase, setPurchase] = useState<Purchase | null>(null);
  const [err, setErr] = useState<Error>();

  async function checkout() {
    try {
      setErr(undefined);
      setPurchase(topup ? await api.topUp(topup, trips)
        : await api.buy({ product_id: product, from_station_id: from, to_station_id: to, device_public_key: await devicePublicKeyB64() }));
    } catch (e) { setErr(e as Error); }
  }

  if (purchase) return <Pay purchase={purchase} cards={cards.data ?? []} onDone={(ticketId) => nav(`/tickets/${ticketId}`, { replace: true })} />;

  const price = topup ? (trips === 10 ? 9500 : 18500) : products.data?.find((x) => x.id === product)?.price_cents ?? 0;
  return (
    <div className="page no-tabs">
      <TopBar title={topup ? `Top up ${topup}` : "Buy a ticket"} />
      {!topup && <div className="card"><span className="muted small">Route</span><h3>{stationName(from)} → {stationName(to)}</h3></div>}
      <h3>{topup ? "How many trips?" : "Choose a ticket"}</h3>
      <div style={{ display: "grid", gap: 10 }}>
        {topup
          ? [10, 20].map((n) => (
            <button key={n} className="card between" style={{ border: trips === n ? "2px solid var(--blue)" : 0 }} onClick={() => setTrips(n)}>
              <b>+{n} trips</b><b>{rand(n === 10 ? 9500 : 18500)}</b></button>))
          : products.data?.map((x) => (
            <button key={x.id} className="card between" style={{ textAlign: "left", border: product === x.id ? "2px solid var(--blue)" : 0 }} onClick={() => setProduct(x.id)}>
              <span><b>{x.name}</b><br /><span className="muted">{x.description}</span></span><b>{rand(x.price_cents)}</b></button>))}
      </div>
      <ErrorNote error={err} />
      <div className="spacer" />
      <div className="between"><span><span className="muted small">Total · sample fare</span><br /><b style={{ fontSize: 20 }}>{rand(price)}</b></span>
        <button className="btn primary" style={{ width: "auto" }} onClick={checkout}>Continue to payment</button></div>
    </div>
  );
}

function Pay({ purchase, cards, onDone }: { purchase: Purchase; cards: SavedCard[]; onDone: (ticketId: string) => void }) {
  const [method, setMethod] = useState<string>(cards[0]?.id ?? "new");
  const [pay, setPay] = useState<Payment | null>(null);
  const [card, setCard] = useState({ number: "4000 0000 0000 0002", expiry: "08/29", cvv: "123", name: "", save: true });
  const [otp, setOtp] = useState("");
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState("");

  async function run<T>(fn: () => Promise<T>) {
    setBusy(true); setErr("");
    try { return await fn(); } catch (e) { setErr((e as ApiError).message); } finally { setBusy(false); }
  }

  async function finish() {
    for (let i = 0; i < 20; i++) { // ticket is issued asynchronously after payment
      const p = await api.purchase(purchase.id);
      if (p.ticket_id) return onDone(p.ticket_id);
      await new Promise((r) => setTimeout(r, 500));
    }
    setErr("Payment received. Your ticket is on its way — check My tickets.");
  }

  if (!pay) {
    return (
      <div className="page no-tabs">
        <TopBar title="Checkout" />
        <div className="card between"><span><span className="muted small">{purchase.description}</span></span><b>{rand(purchase.amount_cents)}</b></div>
        <h3>Pay by card</h3>
        <div className="list">
          {cards.map((c) => (
            <button key={c.id} className="list-row" onClick={() => setMethod(c.id)} aria-pressed={method === c.id}>
              <span>{method === c.id ? "◉" : "○"}</span><span style={{ flex: 1 }}><b>{c.brand.toUpperCase()} ending {c.last4}</b><br /><span className="muted">Saved card · expires {c.expiry}</span></span></button>))}
          <button className="list-row" onClick={() => setMethod("new")} aria-pressed={method === "new"}>
            <span>{method === "new" ? "◉" : "○"}</span><b>New debit or credit card</b></button>
        </div>
        <div className="banner info"><Icon name="lock" size={18} />Card payments are processed securely by Pay@. MetroRail never sees or stores your full card number.</div>
        {err && <p className="error-text">{err}</p>}
        <div className="spacer" />
        <button className="btn primary" disabled={busy} onClick={() => run(async () =>
          setPay(await api.pay(purchase.id, method === "new" ? "new_card" : "saved_card", method === "new" ? undefined : method)))}>
          {method === "new" ? "Continue to card details" : `Pay ${rand(purchase.amount_cents)}`}</button>
      </div>
    );
  }

  if (pay.status === "requires_card") {
    return (
      <div className="page no-tabs">
        <TopBar title="Card details" right={<span className="pill green"><Icon name="lock" size={14} /> Pay@</span>} />
        <div className="card between"><span className="muted small">MetroRail · {purchase.description}</span><b>{rand(purchase.amount_cents)}</b></div>
        {(["number", "name"] as const).map((k) => (
          <div className="field" key={k}><label htmlFor={k}>{k === "number" ? "Card number" : "Name on card"}</label>
            <input id={k} className={`input ${pay.decline_reason === "invalid_card_number" && k === "number" ? "error" : ""}`} value={card[k]}
              inputMode={k === "number" ? "numeric" : "text"} onChange={(e) => setCard({ ...card, [k]: e.target.value })} /></div>
        ))}
        <div className="row">
          <div className="field" style={{ flex: 1 }}><label htmlFor="exp">Expiry (MM/YY)</label><input id="exp" className="input" value={card.expiry} onChange={(e) => setCard({ ...card, expiry: e.target.value })} /></div>
          <div className="field" style={{ flex: 1 }}><label htmlFor="cvv">CVV</label><input id="cvv" className="input" inputMode="numeric" value={card.cvv} onChange={(e) => setCard({ ...card, cvv: e.target.value })} /></div>
        </div>
        {pay.decline_reason && <p className="error-text">{pay.decline_reason === "card_expired" ? "This card has expired." : "Check the card number."}</p>}
        <label className="row small"><input type="checkbox" checked={card.save} onChange={(e) => setCard({ ...card, save: e.target.checked })} />Save this card with Pay@ for faster checkout. MetroRail only keeps the last 4 digits.</label>
        <p className="muted small">Demo cards: 4000 0000 0000 0002 approves · 4000 0000 0000 0069 is declined.</p>
        {err && <p className="error-text">{err}</p>}
        <button className="btn primary" disabled={busy || !card.name} onClick={() => run(async () => setPay(await api.submitCard(pay.id, card)))}>Pay {rand(purchase.amount_cents)}</button>
      </div>
    );
  }

  if (pay.status === "requires_3ds") {
    return (
      <div className="page no-tabs">
        <TopBar title="Bank approval" />
        <h1>Your bank needs to approve this</h1>
        <p className="muted">3-D Secure check · {pay.brand?.toUpperCase()} ending {pay.last4}</p>
        <div className="card"><div className="between"><span className="muted">Merchant</span><b>MetroRail via Pay@</b></div>
          <div className="between" style={{ marginTop: 8 }}><span className="muted">Amount</span><b>{rand(pay.amount_cents)}</b></div></div>
        <div className="field"><label htmlFor="otp">One-time PIN from your bank</label>
          <input id="otp" className="input" inputMode="numeric" maxLength={6} value={otp} onChange={(e) => setOtp(e.target.value.replace(/\D/g, ""))} placeholder="123456 in the demo" /></div>
        {pay.decline_reason === "wrong_otp" && <p className="error-text">That PIN didn't match.</p>}
        {err && <p className="error-text">{err}</p>}
        <button className="btn primary" disabled={busy || otp.length !== 6} onClick={() => run(async () => {
          const r = await api.confirm3ds(pay.id, otp); setPay(r); if (r.status === "succeeded") await finish();
        })}>{busy ? "Waiting for your bank…" : "Approve payment"}</button>
      </div>
    );
  }

  if (pay.status === "declined") {
    return (
      <div className="page no-tabs center" style={{ justifyContent: "center" }}>
        <h1>Your bank declined this payment</h1><p className="muted">No money was taken.</p>
        <button className="btn primary" onClick={() => setPay(null)}>Try another card</button>
      </div>
    );
  }

  return <div className="page no-tabs center" style={{ justifyContent: "center" }}><h1>Payment approved</h1><p className="muted">Getting your ticket ready…</p>{err && <p>{err}</p>}</div>;
}
