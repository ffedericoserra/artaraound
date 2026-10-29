import { defineConfig } from "vite"
import react from "@vitejs/plugin-react"

// The .NET API listens on 8001 in development; set API_TARGET=http://localhost:8000 for Node.
const API_TARGET = process.env.API_TARGET || "http://localhost:8001"

export default defineConfig({
    plugins: [react()],
    // Navigator routes are deep links, so bundle assets must always resolve
    // from the site root rather than from the current museum/visit URL.
    base: "/",
    server: {
        port: 5173,
        proxy: {
            "/api": API_TARGET,
            "/uploads": API_TARGET,
            // ws:true is required — without it the SignalR WebSocket upgrade fails and
            // falls back to long polling, or the session runner never receives an event.
            "/hubs": { target: API_TARGET, ws: true },
        },
    },
    build: {
        outDir: "dist",
        emptyOutDir: true,
    },
})
