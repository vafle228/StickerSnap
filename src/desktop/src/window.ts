import { join } from "node:path";
import { BrowserWindow, nativeTheme, screen, shell } from "electron";
import type { DesktopBridge, ServerArg } from "./bridge.js";
import { isAppUrl, isExternalLink } from "./guard.js";
import { background, initialSize, MIN_SIZE } from "./look.js";
import type { Bounds, ShellState } from "./state.js";
import { ru } from "./strings.js";

const SERVER_ARG: ServerArg = "--stickersnap-server=";

export interface WindowOptions {
  url: string;
  server: DesktopBridge["server"];
  state: ShellState;
  /** src/desktop, or app.asar when installed */
  appPath: string;
  /** F12 opens the developer tools: in development only */
  devTools: boolean;
}

/** Saved bounds only while they are still on a screen: a monitor may have gone since. */
function visible(bounds: Bounds | undefined): Bounds | undefined {
  if (!bounds) return undefined;
  const area = screen.getDisplayMatching(bounds).workArea;
  const overlaps =
    bounds.x < area.x + area.width &&
    bounds.x + bounds.width > area.x &&
    bounds.y < area.y + area.height &&
    bounds.y + bounds.height > area.y;
  return overlaps ? bounds : undefined;
}

export function createWindow(options: WindowOptions): BrowserWindow {
  const { state } = options;
  const appOrigin = new URL(options.url).origin;
  const bounds = visible(state.bounds) ?? initialSize(screen.getPrimaryDisplay().workAreaSize);

  const window = new BrowserWindow({
    ...bounds,
    minWidth: MIN_SIZE.width,
    minHeight: MIN_SIZE.height,
    title: ru.appName,
    icon: join(options.appPath, "assets", process.platform === "win32" ? "icon.ico" : "icon.png"),
    // Shown once the SPA has painted, on the colour of its theme: no white flash (plan, 1b).
    show: false,
    backgroundColor: background(state.theme, nativeTheme.shouldUseDarkColors),
    autoHideMenuBar: true,
    webPreferences: {
      preload: join(import.meta.dirname, "preload.cjs"),
      contextIsolation: true,
      sandbox: true,
      nodeIntegration: false,
      spellcheck: false,
      additionalArguments: [`${SERVER_ARG}${JSON.stringify(options.server)}`],
    },
  });
  if (state.maximized) window.maximize();
  window.once("ready-to-show", () => window.show());

  // The window stays on the app. Links leave for the system browser, anything else is dropped.
  window.webContents.setWindowOpenHandler(({ url }) => {
    if (isExternalLink(url)) void shell.openExternal(url);
    return { action: "deny" };
  });
  window.webContents.on("will-navigate", (event, url) => {
    if (isAppUrl(url, appOrigin)) return;
    event.preventDefault();
    if (isExternalLink(url)) void shell.openExternal(url);
  });
  window.webContents.on("will-attach-webview", (event) => event.preventDefault());

  if (options.devTools) {
    window.webContents.on("before-input-event", (_event, input) => {
      if (input.type === "keyDown" && input.key === "F12") window.webContents.toggleDevTools();
    });
  }

  const remember = () => {
    state.maximized = window.isMaximized();
    if (!state.maximized && !window.isMinimized()) state.bounds = window.getBounds();
  };
  window.on("resize", remember);
  window.on("move", remember);
  window.on("close", remember);

  void window.loadURL(options.url);
  return window;
}
