import { defineConfig, devices } from "@playwright/test";

/* E2E: the critical path in a phone-sized Chromium on the demo data of /?mock, so no bot
   service is needed. Vite runs on its own port, apart from the threads' dev servers. */
const port = Number(process.env.E2E_PORT ?? 5198);
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
    reducedMotion: "reduce",
    actionTimeout: 10_000,
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"], viewport: { width: 390, height: 844 }, hasTouch: true },
    },
  ],
  webServer: {
    command: `npx vite --port ${port}`,
    url: baseURL,
    reuseExistingServer: !process.env.CI,
  },
});
