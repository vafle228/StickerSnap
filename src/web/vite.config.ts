import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { defineConfig, type Plugin } from "vitest/config";
import react from "@vitejs/plugin-react";

// Dev: Vite serves the SPA with HMR and proxies the API and the SignalR hub
// to the ASP.NET server (src/server, `dotnet run` listens on this port).
const serverUrl = process.env.STICKERSNAP_SERVER_URL ?? "http://127.0.0.1:5180";

// Demo data (/?mock) needs the MSW service worker at the site root. It is served from
// node_modules in dev only, so the production build in wwwroot never carries it.
function mockWorker(): Plugin {
  const file = createRequire(import.meta.url).resolve("msw/mockServiceWorker.js");
  return {
    name: "stickersnap-mock-worker",
    apply: "serve",
    configureServer(server) {
      server.middlewares.use("/mockServiceWorker.js", (_req, res) => {
        res.setHeader("Content-Type", "text/javascript");
        res.end(readFileSync(file));
      });
    },
  };
}

export default defineConfig({
  plugins: [react(), mockWorker()],
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
  test: {
    environment: "jsdom",
    include: ["src/**/*.test.{ts,tsx}"],
  },
});
