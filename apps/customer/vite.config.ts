import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

// In dev, /api goes to the gateway (Kong in docker-compose, or tools/dev/run_all.py) on :8080.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: { "/api": "http://localhost:8080" },
  },
});
