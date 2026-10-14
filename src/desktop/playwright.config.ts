import { defineConfig } from "@playwright/test";

/* Smoke e2e of the desktop app: the real shell, the real server, no demo data. It runs the
   unpacked build of electron-builder (STICKERSNAP_APP, as CI does) or, without it, `electron .`
   from here with the server of `npm run server`. */
export default defineConfig({
  testDir: "e2e",
  // One app at a time: each launch starts a server and a window.
  workers: 1,
  timeout: 60_000,
  // The packaged app starts cold on a Windows runner: the first page can take longer than 5 s.
  expect: { timeout: 20_000 },
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [["github"], ["html", { open: "never" }]] : "list",
  use: { trace: "retain-on-failure" },
});
