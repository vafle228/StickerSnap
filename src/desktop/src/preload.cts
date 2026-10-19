/* Runs sandboxed before the SPA, in its own world: it may require nothing but electron, so the
   shared names come in as types (bridge.ts) and are checked here, not imported. */
import electron = require("electron");
import type { DesktopBridge, Invokes, Sends, ServerArg } from "./bridge.js";

const { contextBridge, ipcRenderer, webUtils } = electron;

const SERVER_ARG: ServerArg = "--stickersnap-server=";

function invoke<C extends keyof Invokes>(
  channel: C,
  ...args: Parameters<Invokes[C]>
): Promise<ReturnType<Invokes[C]>> {
  return ipcRenderer.invoke(channel, ...args) as Promise<ReturnType<Invokes[C]>>;
}

function send<C extends keyof Sends>(channel: C, ...args: Parameters<Sends[C]>) {
  ipcRenderer.send(channel, ...args);
}

function server(): DesktopBridge["server"] {
  const arg = process.argv.find((a) => a.startsWith(SERVER_ARG));
  return arg ? (JSON.parse(arg.slice(SERVER_ARG.length)) as DesktopBridge["server"]) : {};
}

const bridge: DesktopBridge = {
  server: server(),
  openFile: (kind) => invoke("dialog:open-file", kind),
  pickFolder: (current) => invoke("dialog:pick-folder", current),
  openFolder: (path) => invoke("shell:open-folder", path),
  showInFolder: (path) => invoke("shell:show-in-folder", path),
  openExternal: (url) => invoke("shell:open-external", url),
  saveFile: (url, fileName) => invoke("file:save", url, fileName),
  setTheme: (theme) => send("theme:set", theme),
  pathForFile: (file) => webUtils.getPathForFile(file),
  lastCrash: () => invoke("crash:take"),
};

contextBridge.exposeInMainWorld("stickersnap", bridge);
