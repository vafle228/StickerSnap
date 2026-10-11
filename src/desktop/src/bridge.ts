/* The narrow bridge the preload puts on window.stickersnap (layout plan, «Мост с Electron»).
   Types only: the sandboxed preload can require nothing but electron, and the SPA imports these
   types as @stickersnap/desktop/bridge to build its Platform. No Node reaches the SPA. */

/** What a file dialog of the SPA asks for */
export type FileKind = "video" | "webm" | "project";

/** The SPA's theme setting; the native parts of the window (title bar, menus) follow it */
export type ThemeSource = "system" | "light" | "dark";

export interface DesktopBridge {
  /** The local server: its origin and the one-time token of this launch. Both are absent in
   *  development, where the window shows Vite on its own origin over the demo data. */
  server: { baseUrl?: string; token?: string };
  /** The system «Open» dialog; the chosen path, or null on cancel */
  openFile(kind: FileKind): Promise<string | null>;
  /** The system folder picker starting at `current`; null on cancel */
  pickFolder(current: string): Promise<string | null>;
  /** Opens a folder in the file manager. Files are never opened, so nothing runs by accident. */
  openFolder(path: string): Promise<void>;
  /** The file manager with the file selected */
  showInFolder(path: string): Promise<void>;
  /** A t.me link or another page in the system browser or the Telegram client */
  openExternal(url: string): Promise<void>;
  /** «Save as» for a file of the local server; resolves when it is written or the user cancels */
  saveFile(url: string, fileName: string): Promise<void>;
  /** The SPA's theme, so the title bar matches it and the next launch paints the right colour */
  setTheme(theme: ThemeSource): void;
  /** The path of a file dropped on the window (webUtils.getPathForFile); "" when it has none */
  pathForFile(file: File): string;
}

/** IPC from the preload to the main process: channel → arguments and result */
export interface Invokes {
  "dialog:open-file": (kind: FileKind) => string | null;
  "dialog:pick-folder": (current: string) => string | null;
  "shell:open-folder": (path: string) => void;
  "shell:show-in-folder": (path: string) => void;
  "shell:open-external": (url: string) => void;
  "file:save": (url: string, fileName: string) => void;
}

/** One-way messages from the preload */
export interface Sends {
  "theme:set": (theme: ThemeSource) => void;
}

/** The renderer's argument that carries `server` (as JSON) from the main process to the preload.
 *  A type, not a constant: each side spells it out and the compiler checks they agree. */
export type ServerArg = "--stickersnap-server=";
