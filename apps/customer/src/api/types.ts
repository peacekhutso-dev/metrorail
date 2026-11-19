// Mirrors the services' response models (contracts/openapi). Keep in sync or generate later.

export type TrainState = "moving" | "at_station" | "held" | "stopped" | "disruption";

export interface Station { id: string; name: string; lat: number; lng: number; lines: string[] }
export interface Departure {
  train_no: string; line_id: string; direction: "southbound" | "northbound";
  terminus: string; platform: string; scheduled: string; scheduled_min: number; in_min: number;
}
export interface JourneyOption {
  train_no: string; line_id: string; from_station: string; to_station: string;
  departs: string; arrives: string; duration_min: number; platform: string; changes: number;
}
export interface StopEta {
  station_id: string; station_name: string; scheduled: string; predicted: string;
  likely_from: string; likely_to: string; delay_min: number; passed: boolean;
}
export interface TrainEta {
  train_no: string; state: TrainState | null; stationary_s: number; live: boolean; model: string;
  stops: StopEta[];
}
export interface JourneyEta {
  train_no: string; state: TrainState | null; stationary_s: number;
  board_station: string; board_scheduled: string; board_predicted: string; board_in_min: number;
  stops_until_board: number; alight_station: string; alight_predicted: string; ride_min: number;
  likely_from: string; likely_to: string;
}
export interface TrainLive {
  train_no: string; lat: number; lng: number; state: TrainState; stationary_s: number;
  last_station_id: string | null; next_station_id: string | null; at_station_id: string | null;
  delay_min: number;
}
export interface SavedTrip {
  id: string; name: string; from_station_id: string; to_station_id: string; days: string[];
  usual_time: string; train_no: string | null; alert_delays: boolean; leave_reminder: boolean;
  leave_minutes: number;
}
export type TripInput = Omit<SavedTrip, "id">;
export interface Alert {
  id: string; kind: string; title: string; body: string; train_no: string | null; read: boolean;
  created_at: string;
}
export interface Product { id: string; name: string; description: string; price_cents: number; trips: number | null }
export interface Purchase { id: string; status: string; amount_cents: number; description: string; ticket_id: string | null }
export interface Ticket {
  id: string; product_id: string; product_name: string; from_station_id: string; to_station_id: string;
  trips_total: number | null; trips_used: number; trips_left: number | null; trip_in_progress: boolean;
  trip_valid_until: string | null; valid_to: string;
  status: "ready" | "in_progress" | "low" | "used_up" | "expired"; cert: string;
}
export interface Payment {
  id: string; purchase_id: string; status: "requires_card" | "requires_3ds" | "succeeded" | "declined";
  amount_cents: number; description: string; brand: string | null; last4: string | null;
  decline_reason: string | null;
}
export interface SavedCard { id: string; brand: string; last4: string; expiry: string }
export interface Me {
  id: string; phone_masked: string | null; email: string | null; plan: string;
  consents: { trip_alerts: boolean; nearest_station: boolean; usage_counts: boolean };
  member_since: string;
}
export interface Tokens { access_token: string; refresh_token: string; user_id: string; is_new_user: boolean }
