import { useState } from "react";
import { useNavigate } from "react-router-dom";

import { ApiError, api, auth } from "../api/client";
import { Icon } from "../components/Icon";
import { TopBar } from "../components/ui";

/** Step 1 (number or email) and step 2 (6-digit code) of passwordless login. */
export function Login() {
  const nav = useNavigate();
  const [mode, setMode] = useState<"phone" | "email">("phone");
  const [value, setValue] = useState("");
  const [challenge, setChallenge] = useState<{ id: string; sentTo: string; dev?: string } | null>(null);
  const [code, setCode] = useState("");
  const [err, setErr] = useState("");
  const [busy, setBusy] = useState(false);

  async function send() {
    setBusy(true); setErr("");
    try {
      const r = await api.startLogin(mode === "phone" ? { phone: value } : { email: value });
      setChallenge({ id: r.challenge_id, sentTo: r.sent_to, dev: r.dev_code });
    } catch (e) { setErr((e as ApiError).message); } finally { setBusy(false); }
  }

  async function verify() {
    if (!challenge) return;
    setBusy(true); setErr("");
    try {
      const t = await api.verifyLogin(challenge.id, code);
      auth.set(t);
      nav(t.is_new_user ? "/consent" : "/", { replace: true });
    } catch (e) { setErr((e as ApiError).message); } finally { setBusy(false); }
  }

  if (challenge) {
    return (
      <div className="page no-tabs">
        <TopBar />
        <p className="muted">Step 2 of 3</p>
        <h1>Enter the code</h1>
        <p style={{ color: "var(--body)" }}>We sent a 6-digit code to <b>{challenge.sentTo}</b>. <button className="btn text" style={{ width: "auto", minHeight: 0, padding: 0 }} onClick={() => setChallenge(null)}>Change</button></p>
        <input className={`input ${err ? "error" : ""}`} inputMode="numeric" autoComplete="one-time-code" maxLength={6}
          placeholder="000000" value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, ""))}
          style={{ letterSpacing: 10, fontSize: 24, textAlign: "center" }} aria-label="6-digit code" />
        {err && <p className="error-text">{err}</p>}
        {challenge.dev && <p className="muted small">Demo build: your code is {challenge.dev}</p>}
        <div className="banner info"><Icon name="lock" size={18} />The code works for 5 minutes. MetroRail will never ask you for it by phone or WhatsApp.</div>
        <button className="btn primary" disabled={code.length !== 6 || busy} onClick={verify}>Verify</button>
      </div>
    );
  }

  return (
    <div className="page no-tabs">
      <TopBar />
      <p className="muted">Step 1 of 3</p>
      <h1>Log in or sign up</h1>
      <p style={{ color: "var(--body)" }}>We'll send you a 6-digit code. No password, and the same steps whether you're new or coming back.</p>
      <div className="seg">
        <button className={mode === "phone" ? "on" : ""} onClick={() => setMode("phone")}>Phone number</button>
        <button className={mode === "email" ? "on" : ""} onClick={() => setMode("email")}>Email</button>
      </div>
      <div className="field">
        <label htmlFor="who">{mode === "phone" ? "Phone number" : "Email"}</label>
        <input id="who" className={`input ${err ? "error" : ""}`} value={value} onChange={(e) => setValue(e.target.value)}
          inputMode={mode === "phone" ? "tel" : "email"} placeholder={mode === "phone" ? "082 456 4567" : "you@example.com"} />
        {err && <p className="error-text">{err}</p>}
        <p className="muted small">{mode === "phone" ? "We'll send the code by SMS. Standard rates may apply." : "We'll email you the code."}</p>
      </div>
      <div className="banner info"><Icon name="lock" size={18} />We only use your number or email to log you in and send trip alerts. It's encrypted and never shared or sold.</div>
      <button className="btn primary" disabled={!value || busy} onClick={send}>Send code</button>
      <button className="btn text" onClick={() => nav("/")}>Continue as guest</button>
    </div>
  );
}
