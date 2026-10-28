/* The desktop shell: starts the local ASP.NET server with a one-time token, shows the SPA it
   serves in one window, and gives the SPA a narrow bridge to the system (preload.cts). */
import { createWriteStream, existsSync, mkdirSync } from "node:fs";
import { join } from "node:path";
import { app, BrowserWindow, dialog, Menu, nativeTheme, session } from "electron";
import type { LastCrash } from "./bridge.js";
import { beginLaunch, serverCrash, windowCrash } from "./crash.js";
import { registerIpc } from "./ipc.js";
import { applicationMenu } from "./menu.js";
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

// Each e2e run starts as a fresh install (e2e/app.spec.ts).
if (process.env.STICKERSNAP_USER_DATA) app.setPath("userData", process.env.STICKERSNAP_USER_DATA);

const stateFile = join(app.getPath("userData"), "state.json");
const logFile = join(app.getPath("logs"), "server.log");

let state: ShellState = { theme: "system" };
/** How the last launch crashed: the SPA takes it once, for «Прошлый запуск завершился ошибкой» */
let crash: LastCrash | undefined;
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
    shellVersion: `Electron ${process.versions.electron}`,
  });
}

async function start() {
  const menu = applicationMenu(process.platform);
  Menu.setApplicationMenu(menu && Menu.buildFromTemplate(menu));
  state = readState(stateFile);
  nativeTheme.themeSource = state.theme;
  crash = beginLaunch(state, new Date());
  save();

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
    const running = server;
    void running.exited.then((code) => {
      if (quitting) return;
      // The next launch offers to report it (artboard 19, section 4)
      state.crash = serverCrash(code, running.output(), new Date());
      save();
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
    takeCrash() {
      const taken = crash ?? null;
      crash = undefined;
      return taken;
    },
  });
  window = createWindow({
    url,
    server: server ? { baseUrl: server.baseUrl, token: server.token } : {},
    state,
    appPath: app.getAppPath(),
    devTools: !app.isPackaged,
  });
  // A dead renderer leaves a blank window: it is reloaded, and the SPA offers to report it.
  window.webContents.on("render-process-gone", (_event, details) => {
    if (details.reason === "clean-exit" || quitting) return;
    crash = windowCrash(details.reason, new Date());
    window?.webContents.reload();
  });
}

/** Writes the state now, so a crash later still finds the mark of this launch. */
function save() {
  try {
    writeState(stateFile, state);
  } catch {
    // A lost mark only loses the offer to report a crash.
  }
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
    // A clean quit: the next launch finds no mark
    state.running = false;
    save();
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
