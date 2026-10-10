import { api, ApiError, call, isProblem } from "../api/client";
import type { SourceRef } from "../api/types";
import { serverConnection } from "./connection";

/* Everything that touches the OS and files goes through Platform (plan, «Браузер в 1a,
   Electron в 1b»). The browser uploads a file to the local server and downloads the .webm;
   Electron (1b) will pass paths and open system dialogs. Screens never ask which one runs. */

export interface Platform {
  kind: "browser" | "electron";
  /** The browser has no file paths: no «Показать в папке», no opening .ssproj by path */
  hasPaths: boolean;
  /** «Выбрать файл»: a video or a GIF for import, null when the user cancels */
  pickVideo(): Promise<SourceRef | null>;
  /** A .webm for the checker (R8) */
  pickWebm(): Promise<SourceRef | null>;
  /** A file dropped on the window */
  sourceOf(file: File): Promise<SourceRef>;
  /** Saves a rendered file (GET /api/v1/files/{fileId}); in the browser it is a download (R9) */
  saveFile(fileUrl: string, fileName: string): Promise<void>;
  /** A t.me link or another external page */
  openExternal(url: string): void;
  /** «Изменить…» of a folder in Settings: the system folder picker, null on cancel.
   *  Only with paths; the browser has none, so Settings lets the user type the path. */
  pickFolder(current: string): Promise<string | null>;
  /** «Открыть» of a folder in Settings, in the system file manager. Only with paths. */
  openFolder(path: string): void;
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

/** POST /api/v1/uploads: the bytes go to the local server, the scene keeps only the uploadId. */
async function upload(file: File): Promise<SourceRef> {
  const { uploadId } = await call(
    api.POST("/api/v1/uploads", {
      // schema.d.ts types the binary part as a string; the serializer sends the File itself
      body: { file: file as unknown as string },
      bodySerializer: () => {
        const form = new FormData();
        form.append("file", file);
        return form;
      },
    }),
  );
  return { kind: "upload", uploadId };
}

async function pickAndUpload(accept: string) {
  const file = await chooseFile(accept);
  return file ? upload(file) : null;
}

export const browserPlatform: Platform = {
  kind: "browser",
  hasPaths: false,
  pickVideo: () => pickAndUpload(VIDEO_TYPES),
  pickWebm: () => pickAndUpload(".webm,video/webm"),
  sourceOf: upload,
  async saveFile(fileUrl, fileName) {
    // The token goes in a header, so the file is fetched first and then handed to the browser.
    const { baseUrl, token } = serverConnection();
    const response = await fetch(new URL(fileUrl, baseUrl), {
      headers: token ? { Authorization: `Bearer ${token}` } : undefined,
    });
    if (!response.ok) {
      const body: unknown = await response.json().catch(() => undefined);
      throw new ApiError(response.status, isProblem(body) ? body : undefined);
    }
    const href = URL.createObjectURL(await response.blob());
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
};

/** The platform this SPA runs on. Stage 1a has only the browser. */
export const platform: Platform = browserPlatform;
