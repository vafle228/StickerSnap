import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { defineConfig, type Plugin } from "vitest/config";
import react from "@vitejs/plugin-react";

// Dev: Vite serves the SPA with HMR and proxies the API and the SignalR hub
// to the ASP.NET server (src/server, `dotnet run` listens on this port).
const serverUrl = process.env.STICKERSNAP_SERVER_URL ?? "http://127.0.0.1:5180";

// Two builds. `npm run build` is the production one: no demo data, no dev bar, no showcase
// (tools/check-prod-build.mjs checks that in CI). `npm run build:dev` (--mode development) keeps
// them for trying the built and packaged app on the demo scenarios. `vite` always has them.
const DEV_BUILD = "development";

// Demo data (/?mock) needs the MSW service worker at the site root: served from node_modules
// by `vite`, written next to index.html by the dev build, absent from the production build.
function mockWorker(): Plugin {
  const file = createRequire(import.meta.url).resolve("msw/mockServiceWorker.js");
  return {
    name: "stickersnap-mock-worker",
    apply: (_config, { command, mode }) => command === "serve" || mode === DEV_BUILD,
    configureServer(server) {
      server.middlewares.use("/mockServiceWorker.js", (_req, res) => {
        res.setHeader("Content-Type", "text/javascript");
        res.end(readFileSync(file));
      });
    },
    generateBundle() {
      this.emitFile({ type: "asset", fileName: "mockServiceWorker.js", source: readFileSync(file) });
    },
  };
}

export default defineConfig(({ command, mode }) => ({
  plugins: [react(), mockWorker()],
  define: {
    // main.tsx loads the demo data, the dev bar and the showcase only when this is true.
    "import.meta.env.DEV_TOOLS": JSON.stringify(command === "serve" || mode === DEV_BUILD),
  },
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
    // Unit tests cover logic without a DOM: stores, key matching, event wiring. Component tests
    // (*.test.tsx) opt into a DOM with `// @vitest-environment happy-dom` at the top of the file.
    include: ["src/**/*.test.{ts,tsx}"],
  },
}));
