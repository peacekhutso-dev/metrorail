import type { StopEta } from "../api/types";

/** Horizontal strip: up to 5 stops around the rider's journey, with the train's position. */
export function StopStrip({ stops, board, alight, nextId, state }: {
  stops: StopEta[]; board: string; alight: string; nextId: string | null; state: string | null;
}) {
  const bi = stops.findIndex((s) => s.station_id === board);
  const ai = stops.findIndex((s) => s.station_id === alight);
  if (bi < 0 || ai < 0) return null;
  const from = Math.max(0, bi - 2);
  const view = stops.slice(from, Math.min(stops.length, Math.max(ai + 1, from + 5)));
  const n = view.length;
  const W = 358, pad = 26, step = (W - pad * 2) / Math.max(n - 1, 1);
  const x = (i: number) => pad + i * step;
  const ni = view.findIndex((s) => s.station_id === nextId);
  const trainX = ni > 0 ? (x(ni - 1) + x(ni)) / 2 : ni === 0 ? pad - 10 : null;
  const stuck = state === "stopped" || state === "disruption";
  const trainFill = stuck ? "var(--red)" : "var(--amber)";
  const vbi = bi - from, vai = ai - from;
  return (
    <svg width="100%" viewBox={`0 0 ${W} 66`} role="img" style={{ display: "block", marginTop: 8 }}
      aria-label={`Stops: ${view.map((s) => s.station_name).join(", ")}`}>
      <line x1={pad} y1={16} x2={W - pad} y2={16} stroke="#4A5360" strokeWidth={3} strokeLinecap="round" />
      <line x1={x(vbi)} y1={16} x2={x(vai)} y2={16} stroke="var(--blue-on-dark)" strokeWidth={3} />
      {trainX !== null && (state === "held" || stuck) && (
        <line x1={trainX} y1={16} x2={x(vbi)} y2={16} stroke={trainFill} strokeWidth={3} strokeDasharray="4 6" />
      )}
      {view.map((s, i) => {
        const isB = i === vbi, isA = i === vai;
        return (
          <g key={s.station_id}>
            <circle cx={x(i)} cy={16} r={isB ? 8 : isA ? 7 : 5}
              fill={isB || isA ? "#fff" : i > vbi && i < vai ? "var(--blue-on-dark)" : "#6B7682"}
              stroke={isB ? "var(--blue-on-dark)" : "none"} strokeWidth={3} />
            <text x={x(i)} y={isB ? 46 : 44} textAnchor="middle" fontSize={isB ? 12 : 10.5}
              fontWeight={isB ? 800 : 600} fill={isB ? "#fff" : "#C9D1DA"}>{s.station_name}</text>
            {isB && <text x={x(i)} y={60} textAnchor="middle" fontSize={9.5} fontWeight={700}
              fill="var(--blue-on-dark)" letterSpacing={0.6}>BOARD HERE</text>}
          </g>
        );
      })}
      {trainX !== null && (
        <circle cx={trainX} cy={16} r={7.5} fill={trainFill}>
          <animate attributeName="r" values="7.5;9;7.5" dur="1.6s" repeatCount="indefinite" />
        </circle>
      )}
    </svg>
  );
}
