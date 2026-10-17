import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { defineConfig, type Plugin } from "vitest/config";
import react from "@vitejs/plugin-react";

// Dev: Vite serves the Mini App with HMR and proxies /v1 to the bot service
// (src/telegram/bot, `uv run stickersnap-bot` listens on this port).
const serviceUrl = process.env.STICKERSNAP_BOT_URL ?? "http://127.0.0.1:8080";

// Two builds, as in src/web. `npm run build` is the production one: no demo data and no dev bar
// (tools/check-prod-build.mjs checks that in CI). `npm run build:dev` (--mode development) keeps
// them for trying the built Mini App on the demo scenarios. `vite` always has them.
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
      this.emitFile({
        type: "asset",
        fileName: "mockServiceWorker.js",
        source: readFileSync(file),
      });
    },
  };
}

export default defineConfig(({ command, mode }) => ({
  plugins: [react(), mockWorker()],
  define: {
    // main.tsx loads the demo data and the dev bar only when this is true.
    "import.meta.env.DEV_TOOLS": JSON.stringify(command === "serve" || mode === DEV_BUILD),
  },
  server: {
    host: "127.0.0.1",
    port: 5174,
    strictPort: true,
    proxy: {
      "/v1": { target: serviceUrl, changeOrigin: true },
    },
  },
  test: {
    // Unit tests run in node; component tests (*.test.tsx) opt into a DOM with
    // `// @vitest-environment happy-dom` at the top of the file, as in src/web.
    include: ["src/**/*.test.{ts,tsx}"],
  },
}));
