import { Navigate, Route, Routes, useLocation } from "react-router-dom";

import { useUserId } from "./auth/useSession";
import { TabBar } from "./components/TabBar";
import { Account } from "./screens/Account";
import { Alerts } from "./screens/Alerts";
import { Buy } from "./screens/Buy";
import { Consent } from "./screens/Consent";
import { Departures } from "./screens/Departures";
import { Home } from "./screens/Home";
import { LiveMap } from "./screens/LiveMap";
import { Login } from "./screens/Login";
import { Plan } from "./screens/Plan";
import { ShowTicket } from "./screens/ShowTicket";
import { Tickets } from "./screens/Tickets";
import { EditTrip, MyTrips } from "./screens/Trips";
import { Welcome } from "./screens/Welcome";

const NO_TABS = ["/welcome", "/login", "/consent", "/buy", "/tickets/", "/trips/"];

function Private({ children }: { children: JSX.Element }) {
  return useUserId() ? children : <Navigate to="/login" replace />;
}

export function App() {
  const { pathname } = useLocation();
  const user = useUserId();
  const firstRun = (() => { try { return !localStorage.getItem("metrorail.welcomed"); } catch { return false; } })();
  const tabs = !NO_TABS.some((p) => pathname.startsWith(p));
  return (
    <div className="app">
      <Routes>
        <Route path="/" element={firstRun && !user ? <Navigate to="/welcome" replace /> : <Home />} />
        <Route path="/welcome" element={<Welcome />} />
        <Route path="/login" element={<Login />} />
        <Route path="/consent" element={<Private><Consent /></Private>} />
        <Route path="/plan" element={<Plan />} />
        <Route path="/departures/:stationId" element={<Departures />} />
        <Route path="/live/:trainNo" element={<LiveMap />} />
        <Route path="/trips" element={<Private><MyTrips /></Private>} />
        <Route path="/trips/:id" element={<Private><EditTrip /></Private>} />
        <Route path="/tickets" element={<Tickets />} />
        <Route path="/tickets/:ticketId" element={<Private><ShowTicket /></Private>} />
        <Route path="/buy" element={<Private><Buy /></Private>} />
        <Route path="/alerts" element={<Alerts />} />
        <Route path="/account" element={<Private><Account /></Private>} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
      {tabs && <TabBar />}
    </div>
  );
}
