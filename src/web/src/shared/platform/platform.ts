import type { LastCrash } from "@stickersnap/desktop/bridge";
import { fetchFile } from "../api/client";
import type { SourceRef, Theme } from "../api/types";
import { serverConnection } from "./connection";
import { electronPlatform } from "./electron";
import { upload } from "./upload";

/* Everything that touches the OS and files goes through Platform (plan, «Браузер в 1a,
   Electron в 1b»). The browser uploads a file to the local server and downloads the .webm;
   Electron passes paths and opens system dialogs (electron.ts). Screens never ask which one runs. */

/** A file the user picked or dropped, before the server has seen it. Its name is known at once,
 *  so «Читаю «cat.gif»» can say which file is read while it is still uploading. */
export interface LocalFile {
  name: string;
  /** The browser's file; Electron has a path instead */
  file?: File;
  path?: string;
}

export interface Platform {
  kind: "browser" | "electron";
  /** The browser has no file paths: no «Показать в папке», no opening .ssproj by path */
  hasPaths: boolean;
  /** «Выбрать файл»: a video or a GIF for import, null when the user cancels */
  pickVideo(): Promise<LocalFile | null>;
  /** A .webm for the checker (R8) */
  pickWebm(): Promise<LocalFile | null>;
  /** «Открыть файл…» in «Проекты»: the path of a .ssproj, null on cancel. Only with paths. */
  pickProject(): Promise<string | null>;
  /** A file dropped on the window */
  dropped(file: File): LocalFile;
  /** What the server reads the file by: a path, or the browser's upload. `signal` stops the
   *  upload («Отменить» while «Читаю файл»). */
  sourceOf(file: LocalFile, signal?: AbortSignal): Promise<SourceRef>;
  /** Saves a rendered file (GET /api/v1/files/{fileId}); in the browser it is a download (R9) */
  saveFile(fileUrl: string, fileName: string): Promise<void>;
  /** A t.me link or another external page */
  openExternal(url: string): void;
  /** «Изменить…» of a folder in Settings: the system folder picker, null on cancel.
   *  Only with paths; the browser has none, so Settings lets the user type the path. */
  pickFolder(current: string): Promise<string | null>;
  /** «Открыть» of a folder in Settings, in the system file manager. Only with paths. */
  openFolder(path: string): void;
  /** «Показать в папке» of a project: the file manager with the file selected. Only with paths. */
  showInFolder(path: string): void;
  /** The theme setting changed: Electron's title bar and next launch follow it */
  themeChanged(mode: Theme): void;
  /** How the last launch crashed, once (artboard 19, section 4); the browser never knows */
  lastCrash(): Promise<LastCrash | null>;
}

const VIDEO_TYPES = ".mp4,.mov,.webm,.gif,.webp,video/*,image/gif,image/webp";

/** The system file picker; resolves null on cancel. */
function chooseFile(accept: string): Promise<File | null> {
  return new Promise((resolve) => {
    const input = document.createElement("input");
    input.type = "file";
    input.accept = accept;
    input.addEventListener("change", () => resolve(input.files?.[0] ?? null), { once: true });
    input.addEventListener("cancel", () => resolve(null), { once: true });
    input.click();
  });
}

async function pick(accept: string): Promise<LocalFile | null> {
  const file = await chooseFile(accept);
  return file ? { name: file.name, file } : null;
}

export const browserPlatform: Platform = {
  kind: "browser",
  hasPaths: false,
  pickVideo: () => pick(VIDEO_TYPES),
  pickWebm: () => pick(".webm,video/webm"),
  pickProject: () => Promise.resolve(null),
  dropped: (file) => ({ name: file.name, file }),
  sourceOf: upload,
  async saveFile(fileUrl, fileName) {
    // The token goes in a header, so the file is fetched first and then handed to the browser.
    const href = URL.createObjectURL(await fetchFile(fileUrl));
    const link = document.createElement("a");
    link.href = href;
    link.download = fileName;
    link.click();
    setTimeout(() => URL.revokeObjectURL(href), 0);
  },
  openExternal(url) {
    window.open(url, "_blank", "noopener,noreferrer");
  },
  pickFolder: () => Promise.resolve(null),
  openFolder: () => {},
  showInFolder: () => {},
  themeChanged: () => {},
  lastCrash: () => Promise.resolve(null),
};

/** The platform this SPA runs on: Electron's when the desktop shell's preload is there. */
export const platform: Platform = globalThis.stickersnap
  ? electronPlatform(globalThis.stickersnap, serverConnection().baseUrl)
  : browserPlatform;
