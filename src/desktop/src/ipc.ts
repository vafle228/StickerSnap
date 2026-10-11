import { stat, writeFile } from "node:fs/promises";
import { join } from "node:path";
import {
  app,
  BrowserWindow,
  dialog,
  ipcMain,
  net,
  shell,
  type FileFilter,
  type IpcMainEvent,
  type IpcMainInvokeEvent,
} from "electron";
import type { FileKind, Invokes, Sends, ThemeSource } from "./bridge.js";
import { isAppUrl, isExternalLink, isServerFile, safeFileName } from "./guard.js";
import { ru } from "./strings.js";

export interface IpcContext {
  /** Origin of the page the window shows: only it may call the shell */
  appOrigin: string;
  /** The local server, when the shell started one */
  server?: { baseUrl: string; token: string };
  onTheme(theme: ThemeSource): void;
}

const VIDEO = ["mp4", "m4v", "mov", "webm", "mkv", "avi", "gif", "webp"];

const OPEN: Record<FileKind, { title: string; filters: FileFilter[] }> = {
  video: {
    title: ru.openVideo,
    filters: [
      { name: ru.filterVideo, extensions: VIDEO },
      { name: ru.filterAll, extensions: ["*"] },
    ],
  },
  webm: { title: ru.openWebm, filters: [{ name: ru.filterWebm, extensions: ["webm"] }] },
  project: { title: ru.openProject, filters: [{ name: ru.filterProject, extensions: ["ssproj"] }] },
};

const isString = (v: unknown): v is string => typeof v === "string";

class IpcRefused extends Error {}

export function registerIpc(ctx: IpcContext) {
  const trusted = (event: IpcMainEvent | IpcMainInvokeEvent) => {
    const url = event.senderFrame?.url;
    return url !== undefined && isAppUrl(url, ctx.appOrigin);
  };

  /** Each channel checks who asks and what it was given: the arguments arrive untyped. */
  function handle<C extends keyof Invokes>(
    channel: C,
    valid: (args: unknown[]) => args is Parameters<Invokes[C]>,
    run: (
      window: BrowserWindow | null,
      ...args: Parameters<Invokes[C]>
    ) => Promise<ReturnType<Invokes[C]>>,
  ) {
    ipcMain.handle(channel, (event, ...args: unknown[]) => {
      if (!trusted(event) || !valid(args)) throw new IpcRefused(`${channel} refused`);
      return run(BrowserWindow.fromWebContents(event.sender), ...args);
    });
  }

  function on<C extends keyof Sends>(
    channel: C,
    valid: (args: unknown[]) => args is Parameters<Sends[C]>,
    run: (...args: Parameters<Sends[C]>) => void,
  ) {
    ipcMain.on(channel, (event: IpcMainEvent, ...args: unknown[]) => {
      if (trusted(event) && valid(args)) run(...args);
    });
  }

  const oneString = (args: unknown[]): args is [string] => args.length === 1 && isString(args[0]);

  handle(
    "dialog:open-file",
    (args): args is [FileKind] => args.length === 1 && isString(args[0]) && Object.hasOwn(OPEN, args[0]),
    async (window, kind) => {
      const options = { ...OPEN[kind], properties: ["openFile" as const] };
      const result = window
        ? await dialog.showOpenDialog(window, options)
        : await dialog.showOpenDialog(options);
      return result.canceled ? null : (result.filePaths[0] ?? null);
    },
  );

  handle("dialog:pick-folder", oneString, async (window, current) => {
    const options = {
      title: ru.pickFolder,
      defaultPath: current || undefined,
      properties: ["openDirectory" as const, "createDirectory" as const],
    };
    const result = window
      ? await dialog.showOpenDialog(window, options)
      : await dialog.showOpenDialog(options);
    return result.canceled ? null : (result.filePaths[0] ?? null);
  });

  // Only a folder: shell.openPath on a file would run it with its default program.
  handle("shell:open-folder", oneString, async (_window, path) => {
    const info = await stat(path).catch(() => null);
    if (info?.isDirectory()) await shell.openPath(path);
  });

  handle("shell:show-in-folder", oneString, async (_window, path) => {
    shell.showItemInFolder(path);
  });

  handle("shell:open-external", oneString, async (_window, url) => {
    if (isExternalLink(url)) await shell.openExternal(url);
  });

  handle(
    "file:save",
    (args): args is [string, string] => args.length === 2 && args.every(isString),
    async (window, url, fileName) => {
      const { server } = ctx;
      if (!server || !isServerFile(url, new URL(server.baseUrl).origin)) return;
      const options = {
        title: ru.saveFile,
        defaultPath: join(app.getPath("downloads"), safeFileName(fileName, "sticker.webm")),
        filters: [{ name: ru.filterWebm, extensions: ["webm"] }],
      };
      const target = window
        ? await dialog.showSaveDialog(window, options)
        : await dialog.showSaveDialog(options);
      if (target.canceled || !target.filePath) return;
      const response = await net.fetch(url, {
        headers: { Authorization: `Bearer ${server.token}` },
      });
      if (!response.ok) throw new Error(`GET ${new URL(url).pathname}: HTTP ${response.status}`);
      await writeFile(target.filePath, Buffer.from(await response.arrayBuffer()));
    },
  );

  on(
    "theme:set",
    (args): args is [ThemeSource] =>
      args.length === 1 && (args[0] === "system" || args[0] === "light" || args[0] === "dark"),
    (theme) => ctx.onTheme(theme),
  );
}
