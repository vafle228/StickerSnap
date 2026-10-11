/* The desktop shell: starts the local ASP.NET server with a one-time token, shows the SPA it
   serves in one window, and gives the SPA a narrow bridge to the system (preload.cts). */
import { createWriteStream, existsSync, mkdirSync } from "node:fs";
import { join } from "node:path";
import { app, BrowserWindow, dialog, Menu, nativeTheme, session } from "electron";
import { registerIpc } from "./ipc.js";
import {
  choosePort,
  newToken,
  serverCommand,
  ServerStartError,
  startServer,
  type RunningServer,
} from "./server.js";
import { readState, writeState, type ShellState } from "./state.js";
import { ru } from "./strings.js";
import { createWindow } from "./window.js";

/** `--dev-url=<vite>/?mock=desktop` shows Vite over the demo data and starts no server. */
const devUrl = process.argv.find((a) => a.startsWith("--dev-url="))?.slice("--dev-url=".length);

const stateFile = join(app.getPath("userData"), "state.json");
const logFile = join(app.getPath("logs"), "server.log");

let state: ShellState = { theme: "system" };
let server: RunningServer | undefined;
let window: BrowserWindow | null = null;
let quitting = false;

function fail(title: string, message: string, output = "") {
  const details = [message, output.trim(), ru.logHint(logFile)].filter(Boolean).join("\n\n");
  dialog.showErrorBox(title, details);
}

async function launchServer(): Promise<RunningServer> {
  const port = await choosePort(state.port);
  state.port = port;
  mkdirSync(join(logFile, ".."), { recursive: true });
  const log = createWriteStream(logFile, { flags: "w" });
  const command = serverCommand({
    packaged: app.isPackaged,
    resourcesPath: process.resourcesPath,
    appPath: app.getAppPath(),
    platform: process.platform,
    arch: process.arch,
    exists: existsSync,
  });
  return startServer(command, {
    port,
    token: newToken(),
    log,
    // The installed server starts in a second; `dotnet run` from the repository builds first.
    timeoutMs: app.isPackaged ? 30_000 : 180_000,
  });
}

async function start() {
  Menu.setApplicationMenu(null);
  state = readState(stateFile);
  nativeTheme.themeSource = state.theme;

  // The SPA needs no camera, microphone, notifications or location.
  const allowed = new Set(["clipboard-sanitized-write"]);
  session.defaultSession.setPermissionRequestHandler((_wc, permission, done) =>
    done(allowed.has(permission)),
  );
  session.defaultSession.setPermissionCheckHandler((_wc, permission) => allowed.has(permission));

  if (!devUrl) {
    try {
      server = await launchServer();
    } catch (error) {
      const output = error instanceof ServerStartError ? error.output : "";
      fail(ru.serverFailedTitle, ru.serverFailed, output || String(error));
      app.exit(1);
      return;
    }
    void server.exited.then(() => {
      if (quitting) return;
      fail(ru.serverStoppedTitle, ru.serverStopped);
      app.quit();
    });
  }

  const url = devUrl ?? `${server?.baseUrl ?? ""}/`;
  registerIpc({
    appOrigin: new URL(url).origin,
    server: server && { baseUrl: server.baseUrl, token: server.token },
    onTheme(theme) {
      state.theme = theme;
      nativeTheme.themeSource = theme;
    },
  });
  window = createWindow({
    url,
    server: server ? { baseUrl: server.baseUrl, token: server.token } : {},
    state,
    appPath: app.getAppPath(),
    devTools: !app.isPackaged,
  });
}

if (!app.requestSingleInstanceLock()) {
  // One window over one server: a second launch brings the first to the front.
  app.quit();
} else {
  if (process.platform === "win32") app.setAppUserModelId("app.stickersnap.desktop");
  app.on("second-instance", () => {
    if (!window) return;
    if (window.isMinimized()) window.restore();
    window.focus();
  });
  app.on("window-all-closed", () => app.quit());
  app.on("before-quit", () => {
    quitting = true;
    try {
      writeState(stateFile, state);
    } catch {
      // A lost window size is no reason to stop quitting.
    }
  });
  // The server goes down with the window. If the shell crashes instead, the server notices
  // its parent is gone and quits by itself.
  app.on("will-quit", (event) => {
    const running = server;
    if (!running) return;
    server = undefined;
    event.preventDefault();
    void running.stop().finally(() => app.quit());
  });
  void app.whenReady().then(start);
}
