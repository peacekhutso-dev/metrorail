import { useEffect, useState } from "react";

import { api } from "../api/client";
import type { TrainLive } from "../api/types";

/**
 * Live train updates over Centrifugo's unidirectional SSE (plain EventSource — works through
 * mobile networks and proxies). Falls back to polling tracking every 15 s if SSE is unavailable.
 * `onAlert` receives in-app alerts for the logged-in user.
 */
export function useLiveTrains(trains: string[], onAlert?: (a: { title: string; body: string }) => void) {
  const [live, setLive] = useState<Record<string, TrainLive>>({});
  const key = trains.join(",");

  useEffect(() => {
    if (!key) return;
    let es: EventSource | null = null;
    let poll: ReturnType<typeof setInterval> | null = null;
    let closed = false;

    const pollOnce = async () => {
      for (const t of key.split(",")) {
        try {
          const v = await api.train(t);
          setLive((m) => ({ ...m, [t]: v }));
        } catch { /* no live data yet */ }
      }
    };
    pollOnce();

    api.realtimeToken(key.split(",")).then(({ token, sse_url }) => {
      if (closed) return;
      es = new EventSource(`${sse_url}?cf_connect=${encodeURIComponent(JSON.stringify({ token }))}`);
      es.onmessage = (m) => {
        const msg = JSON.parse(m.data);
        const pub = msg.pub ?? msg.push?.pub;
        const channel: string | undefined = msg.channel ?? msg.push?.channel;
        if (!pub || !channel) return;
        if (channel.startsWith("trains:")) {
          setLive((cur) => ({ ...cur, [pub.data.train_no]: { ...cur[pub.data.train_no], ...pub.data } }));
        } else if (channel.startsWith("alerts:") && onAlert) {
          onAlert(pub.data);
        }
      };
      es.onerror = () => {
        es?.close();
        if (!poll) poll = setInterval(pollOnce, 15000);
      };
    }).catch(() => { if (!poll) poll = setInterval(pollOnce, 15000); });

    return () => {
      closed = true;
      es?.close();
      if (poll) clearInterval(poll);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);

  return live;
}
