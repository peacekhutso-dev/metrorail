import { NavLink } from "react-router-dom";

import { api } from "../api/client";
import { useAsync } from "../api/useAsync";
import { useUserId } from "../auth/useSession";
import { Icon } from "./Icon";

export function TabBar() {
  const user = useUserId();
  const unread = useAsync(() => (user ? api.unread() : Promise.resolve({ unread: 0 })), [user], 30000);
  const tabs = [
    { to: "/", icon: "home", label: "Home", end: true },
    { to: "/plan", icon: "plan", label: "Plan" },
    { to: "/tickets", icon: "ticket", label: "Tickets" },
    { to: "/alerts", icon: "bell", label: "Alerts", badge: unread.data?.unread },
    user ? { to: "/account", icon: "user", label: "Account" } : { to: "/login", icon: "user", label: "Log in" },
  ];
  return (
    <div className="tabbar">
      <nav aria-label="Main">
        {tabs.map((t) => (
          <NavLink key={t.to} to={t.to} end={t.end} className={({ isActive }) => (isActive ? "active" : "")}>
            <Icon name={t.icon} />
            {t.label}
            {!!t.badge && <span className="badge">{t.badge}</span>}
          </NavLink>
        ))}
      </nav>
    </div>
  );
}
