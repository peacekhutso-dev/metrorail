import type { ReactNode } from "react";
import { useNavigate } from "react-router-dom";

import type { TrainState } from "../api/types";
import { Icon } from "./Icon";

export function TopBar({ title, close, right }: { title?: string; close?: boolean; right?: ReactNode }) {
  const nav = useNavigate();
  return (
    <div className="topbar">
      <button className="icon-btn flat" aria-label={close ? "Close" : "Back"} onClick={() => nav(-1)}>
        <Icon name={close ? "close" : "back"} />
      </button>
      {title && <h2 style={{ flex: 1 }}>{title}</h2>}
      {right}
    </div>
  );
}

export function StatusPill({ state, delay }: { state: TrainState | null | undefined; delay: number }) {
  if (state === "stopped" || state === "disruption") return <span className="pill red"><span className="dot" />Not moving</span>;
  if (state === "held") return <span className="pill amber"><span className="dot" />Held</span>;
  if (delay >= 2) return <span className="pill amber">{delay} min late</span>;
  return <span className="pill green">On time</span>;
}

export function Switch({ on, onChange, label }: { on: boolean; onChange: (v: boolean) => void; label: string }) {
  return (
    <button type="button" role="switch" aria-checked={on} aria-label={label} className="switch" onClick={() => onChange(!on)}>
      <span />
    </button>
  );
}

export function Skeleton({ h = 120 }: { h?: number }) {
  return <div className="skeleton" style={{ height: h }} aria-hidden="true" />;
}

export function ErrorNote({ error }: { error?: Error }) {
  if (!error) return null;
  return <div className="banner red"><Icon name="warn" size={18} />{error.message}</div>;
}

export const rand = (cents: number) => `R${(cents / 100).toFixed(2)}`;

export function stationName(id: string | null | undefined): string {
  if (!id) return "";
  return id.replace(/^ST-/, "").toLowerCase().replace(/(^|-)([a-z])/g, (_, s, c) => (s ? " " : "") + c.toUpperCase())
    .replace("Simons Town", "Simon's Town");
}
