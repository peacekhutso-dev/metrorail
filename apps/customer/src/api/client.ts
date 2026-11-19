import type * as T from "./types";

const BASE = import.meta.env.VITE_API_URL ?? "/api";
const KEY = "metrorail.session";

export class ApiError extends Error {
  constructor(public code: string, message: string, public status: number) {
    super(message);
  }
}

interface Session { access: string; refresh: string; userId: string }

let session: Session | null = (() => {
  try {
    return JSON.parse(localStorage.getItem(KEY) ?? "null");
  } catch {
    return null;
  }
})();
const listeners = new Set<() => void>();

export const auth = {
  get user() { return session?.userId ?? null; },
  set(t: T.Tokens) {
    session = { access: t.access_token, refresh: t.refresh_token, userId: t.user_id };
    try { localStorage.setItem(KEY, JSON.stringify(session)); } catch { /* private mode */ }
    listeners.forEach((l) => l());
  },
  clear() {
    session = null;
    try { localStorage.removeItem(KEY); } catch { /* ignore */ }
    listeners.forEach((l) => l());
  },
  subscribe(fn: () => void) { listeners.add(fn); return () => listeners.delete(fn); },
};

async function refresh(): Promise<boolean> {
  if (!session) return false;
  const r = await fetch(`${BASE}/identity/auth/refresh`, {
    method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ refresh_token: session.refresh }),
  });
  if (!r.ok) { auth.clear(); return false; }
  auth.set(await r.json());
  return true;
}

async function call<R>(method: string, path: string, body?: unknown, retry = true): Promise<R> {
  const headers: Record<string, string> = { "Content-Type": "application/json" };
  if (session) headers.Authorization = `Bearer ${session.access}`;
  const r = await fetch(`${BASE}${path}`, { method, headers, body: body ? JSON.stringify(body) : undefined });
  if (r.status === 401 && retry && session && !path.startsWith("/identity/auth")) {
    if (await refresh()) return call<R>(method, path, body, false);
  }
  if (r.status === 204) return undefined as R;
  const data = await r.json().catch(() => ({}));
  if (!r.ok) {
    const e = data?.error ?? {};
    throw new ApiError(e.code ?? "error", e.message ?? `Request failed (${r.status})`, r.status);
  }
  return data as R;
}

const get = <R>(p: string) => call<R>("GET", p);
const post = <R>(p: string, b?: unknown) => call<R>("POST", p, b);
const qs = (o: Record<string, string | number | undefined>) =>
  "?" + new URLSearchParams(Object.entries(o).filter(([, v]) => v !== undefined) as [string, string][]);

export const api = {
  // identity
  startLogin: (b: { phone?: string; email?: string }) =>
    post<{ challenge_id: string; sent_to: string; expires_in_s: number; dev_code?: string }>("/identity/auth/start", b),
  verifyLogin: (challenge_id: string, code: string) => post<T.Tokens>("/identity/auth/verify", { challenge_id, code }),
  me: () => get<T.Me>("/identity/me"),
  setConsents: (c: T.Me["consents"]) => call<T.Me>("PUT", "/identity/me/consents", c),
  exportData: () => get<unknown>("/identity/me/export"),
  deleteAccount: () => call<void>("DELETE", "/identity/me"),
  logout: async () => {
    if (session) await post("/identity/auth/logout", { refresh_token: session.refresh }).catch(() => {});
    auth.clear();
  },
  // network
  stations: (q?: string) => get<T.Station[]>("/network/stations" + (q ? qs({ q }) : "")),
  nearest: (lat: number, lng: number) => get<T.Station>("/network/stations/nearest" + qs({ lat, lng })),
  departures: (stationId: string, direction?: string, limit = 6) =>
    get<T.Departure[]>(`/network/stations/${stationId}/departures` + qs({ direction, limit })),
  journeys: (from: string, to: string, limit = 4) => get<T.JourneyOption[]>("/network/journeys" + qs({ from, to, limit })),
  // live
  train: (no: string) => get<T.TrainLive>(`/tracking/trains/${no}`),
  trainEta: (no: string) => get<T.TrainEta>(`/eta/trains/${no}/eta`),
  journeyEta: (no: string, board: string, alight: string) =>
    get<T.JourneyEta>(`/eta/trains/${no}/journey` + qs({ board, alight })),
  report: (train_no: string, station_id: string, kind: string) =>
    post("/tracking/reports", { train_no, station_id, kind }),
  realtimeToken: (trains: string[]) =>
    get<{ token: string; sse_url: string; channels: string[] }>("/realtime/token" + qs({ trains: trains.join(",") })),
  // trips
  trips: () => get<T.SavedTrip[]>("/trips/trips"),
  addTrip: (t: T.TripInput) => post<T.SavedTrip>("/trips/trips", t),
  editTrip: (id: string, t: T.TripInput) => call<T.SavedTrip>("PUT", `/trips/trips/${id}`, t),
  deleteTrip: (id: string) => call<void>("DELETE", `/trips/trips/${id}`),
  // alerts
  alerts: () => get<T.Alert[]>("/notifications/alerts"),
  unread: () => get<{ unread: number }>("/notifications/alerts/unread-count"),
  readAll: () => post<void>("/notifications/alerts/read-all"),
  // tickets & payments
  products: () => get<T.Product[]>("/ticketing/products"),
  ticketKeys: () => get<{ issuer_ed25519: string; scanner_x25519: string }>("/ticketing/keys"),
  buy: (b: { product_id: string; from_station_id: string; to_station_id: string; device_public_key: string }) =>
    post<T.Purchase>("/ticketing/purchases", b),
  topUp: (ticketId: string, trips: number) => post<T.Purchase>(`/ticketing/tickets/${ticketId}/topup`, { trips }),
  purchase: (id: string) => get<T.Purchase>(`/ticketing/purchases/${id}`),
  tickets: () => get<T.Ticket[]>("/ticketing/tickets"),
  ticket: (id: string) => get<T.Ticket>(`/ticketing/tickets/${id}`),
  cards: () => get<T.SavedCard[]>("/billing/cards"),
  pay: (purchase_id: string, method: "new_card" | "saved_card", saved_card_id?: string) =>
    post<T.Payment>("/billing/payments", { purchase_id, method, saved_card_id }),
  submitCard: (id: string, card: { number: string; expiry: string; cvv: string; name: string; save: boolean }) =>
    post<T.Payment>(`/billing/payments/${id}/mock-card`, card),
  confirm3ds: (id: string, otp: string) => post<T.Payment>(`/billing/payments/${id}/mock-3ds`, { otp }),
};
