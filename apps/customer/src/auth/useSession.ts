import { useSyncExternalStore } from "react";

import { auth } from "../api/client";

/** Logged-in user id, or null for guests. Re-renders on login/logout. */
export function useUserId(): string | null {
  return useSyncExternalStore(auth.subscribe, () => auth.user);
}
