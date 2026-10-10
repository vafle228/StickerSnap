import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Dev: Vite serves the SPA with HMR and proxies the API and the SignalR hub
// to the ASP.NET server (src/server, `dotnet run` listens on this port).
const serverUrl = process.env.STICKERSNAP_SERVER_URL ?? "http://127.0.0.1:5180";

export default defineConfig({
  plugins: [react()],
  server: {
    host: "127.0.0.1",
    port: 5173,
    strictPort: true,
    proxy: {
      "/api": { target: serverUrl, changeOrigin: true },
      "/hubs": { target: serverUrl, changeOrigin: true, ws: true },
    },
  },
  build: {
    // ASP.NET serves the built SPA from its wwwroot.
    outDir: fileURLToPath(new URL("../server/wwwroot", import.meta.url)),
    emptyOutDir: true,
  },
});
