import { defineConfig, devices } from "@playwright/test";
import { ffmpegDir, work } from "./e2e-server/server";

/* E2E on the real server: the critical path «open a video → edit → exact preview → export»
   against ASP.NET with FFmpeg, on the production build of the SPA it serves (no ?mock, no dev
   bar). Slow by nature, so only that path lives here; every branch of it stays in e2e/ on the
   demo data. Needs `npm run build` and `npm run fetch:ffmpeg`. Port 5198: 5199 is the demo e2e. */
const port = Number(process.env.E2E_SERVER_PORT ?? 5198);
const baseURL = `http://127.0.0.1:${port}`;
// CI builds the server in the build stage and hands it over
const noBuild = process.env.CI ? " --no-build" : "";

export default defineConfig({
  testDir: "e2e-server",
  // One server and one data directory for every spec
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [["github"], ["html", { open: "never" }]] : "list",
  // A real render takes seconds, a cold server more
  timeout: 120_000,
  expect: { timeout: 30_000 },
  use: {
    baseURL,
    locale: "ru-RU",
    trace: "retain-on-failure",
    reducedMotion: "reduce",
    actionTimeout: 10_000,
    acceptDownloads: true,
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"], viewport: { width: 1440, height: 900 } },
    },
  ],
  webServer: {
    command: `node e2e-server/start.mjs "${work}" "${ffmpegDir()}" ${port}${noBuild}`,
    url: `${baseURL}/api/v1/app`,
    // dotnet run builds the server first when CI did not
    timeout: 180_000,
    reuseExistingServer: false,
    stdout: "pipe",
  },
});
