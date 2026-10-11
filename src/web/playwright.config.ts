import { defineConfig, devices } from "@playwright/test";

/* E2E: a few whole user paths in a real browser, on the demo data of /?mock=<scenario>, so no
   server is needed. Vite runs on its own port: 5173 and the ports after it belong to the dev
   servers of the threads. */
const port = Number(process.env.E2E_PORT ?? 5199);
const baseURL = `http://127.0.0.1:${port}`;

export default defineConfig({
  testDir: "e2e",
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [["github"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL,
    locale: "ru-RU",
    trace: "retain-on-failure",
    // Calm motion: no camera moves or glides under a click («Движение в интерфейсе», 6).
    // e2e/motion.spec.ts turns it back on to check the motion itself.
    reducedMotion: "reduce",
    actionTimeout: 10_000,
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"], viewport: { width: 1440, height: 900 } },
    },
  ],
  webServer: {
    command: `npx vite --port ${port}`,
    url: baseURL,
    reuseExistingServer: !process.env.CI,
  },
});
