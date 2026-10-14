import { mkdtempSync, readFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { _electron as electron, expect, test, type ElectronApplication } from "@playwright/test";
import type { DesktopBridge } from "../src/bridge.js";

// No trailing slash: on Windows a trailing backslash escapes the closing quote of the argument.
const appDir = dirname(dirname(fileURLToPath(import.meta.url)));

/** The FFmpeg the app ships: the version pinned in tools/fetch-ffmpeg.mjs, without its "n" */
const pinnedFfmpeg = /const VERSION = "n?([^"]+)"/.exec(
  readFileSync(join(appDir, "..", "..", "tools", "fetch-ffmpeg.mjs"), "utf8"),
)?.[1];

/** The app as a user starts it, on a fresh profile unless `userData` is given. */
function launch(userData = mkdtempSync(join(tmpdir(), "stickersnap-e2e-"))) {
  const env = { ...process.env, STICKERSNAP_USER_DATA: userData } as Record<string, string>;
  const app = process.env.STICKERSNAP_APP
    ? electron.launch({ executablePath: process.env.STICKERSNAP_APP, env })
    : electron.launch({ args: [appDir], env });
  return { app, userData };
}

async function serverIsDown(origin: string) {
  await expect
    .poll(
      () =>
        fetch(origin).then(
          () => "up",
          () => "down",
        ),
      { timeout: 10_000 },
    )
    .toBe("down");
}

let running: ElectronApplication | undefined;

test.afterEach(async () => {
  await running?.close();
  running = undefined;
});

test("opens the SPA from its own server, which lets in only this window", async () => {
  running = await launch().app;
  const page = await running.firstWindow();

  await expect(page).toHaveTitle("StickerSnap");
  await expect(page.getByRole("navigation", { name: "Разделы приложения" })).toBeVisible();

  const origin = new URL(page.url()).origin;
  const seen = await page.evaluate(() => {
    const bridge = (window as unknown as { stickersnap?: DesktopBridge }).stickersnap;
    return {
      server: bridge?.server,
      node: typeof (globalThis as { require?: unknown }).require,
      process: typeof (globalThis as { process?: unknown }).process,
    };
  });
  // The preload hands over the server and the token; Node never reaches the page.
  expect(seen.server?.baseUrl).toBe(origin);
  expect(seen.server?.token).toMatch(/^[\w-]{43}$/);
  expect(seen.node).toBe("undefined");
  expect(seen.process).toBe("undefined");

  const statuses = await page.evaluate(async (token) => {
    const bare = await fetch("/api/v1/app");
    const signed = await fetch("/api/v1/app", { headers: { Authorization: `Bearer ${token}` } });
    return [bare.status, signed.status];
  }, seen.server?.token ?? "");
  expect(statuses[0]).toBe(401);
  expect(statuses[1]).not.toBe(401);
});

test("renders with the FFmpeg it ships, not whatever is on PATH", async () => {
  running = await launch().app;
  const page = await running.firstWindow();
  await expect(page.getByRole("navigation", { name: "Разделы приложения" })).toBeVisible();

  const ffmpeg = await page.evaluate(async () => {
    const bridge = (window as unknown as { stickersnap?: DesktopBridge }).stickersnap;
    const response = await fetch("/api/v1/app", {
      headers: { Authorization: `Bearer ${bridge?.server.token ?? ""}` },
    });
    return ((await response.json()) as { ffmpeg: { status: string; version?: string } }).ffmpeg;
  });
  expect(pinnedFfmpeg).toBeTruthy();
  expect(ffmpeg.status).toBe("ok");
  expect(ffmpeg.version).toContain(pinnedFfmpeg);
});

test("never leaves the app for a file and keeps one window", async () => {
  running = await launch().app;
  const page = await running.firstWindow();
  await expect(page.getByRole("navigation", { name: "Разделы приложения" })).toBeVisible();

  const opened = await page.evaluate(() => window.open("file:///") === null);
  expect(opened).toBe(true);
  expect(running.windows()).toHaveLength(1);
});

test("stops its server on quit and comes back on the same address", async () => {
  const first = launch();
  running = await first.app;
  const page = await running.firstWindow();
  const origin = new URL(page.url()).origin;

  await running.close();
  running = undefined;
  await serverIsDown(origin);

  // The same origin keeps the SPA's localStorage: the open section and the theme.
  running = await launch(first.userData).app;
  const again = await running.firstWindow();
  expect(new URL(again.url()).origin).toBe(origin);
});
