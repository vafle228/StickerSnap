import type { DesktopBridge, FileKind } from "@stickersnap/desktop/bridge";
import type { LocalFile, Platform } from "./platform";
import { upload } from "./upload";

declare global {
  /** Put there by the desktop shell's preload (src/desktop/src/preload.cts); absent in a browser */
  var stickersnap: DesktopBridge | undefined;
}

/** The name the screens show for a path: «cat.gif» of C:\clips\cat.gif or /home/me/cat.gif */
export const fileName = (path: string) => path.split(/[\\/]/).pop() || path;

/* Electron (1b): files stay where they are and the server reads them by path, so nothing is
   uploaded; dialogs and the file manager are the system's. */
export function electronPlatform(bridge: DesktopBridge, baseUrl: string): Platform {
  const open = async (kind: FileKind): Promise<LocalFile | null> => {
    const path = await bridge.openFile(kind);
    return path ? { name: fileName(path), path } : null;
  };

  return {
    kind: "electron",
    hasPaths: true,
    pickVideo: () => open("video"),
    pickWebm: () => open("webm"),
    pickProject: () => bridge.openFile("project"),
    dropped(file) {
      const path = bridge.pathForFile(file);
      // A file without a path (dragged out of another app's memory) still uploads.
      return path ? { name: file.name, path } : { name: file.name, file };
    },
    // A path goes as it is; only a drop without one is uploaded.
    sourceOf: upload,
    saveFile: (fileUrl, name) => bridge.saveFile(new URL(fileUrl, baseUrl).href, name),
    openExternal: (url) => void bridge.openExternal(url),
    pickFolder: (current) => bridge.pickFolder(current),
    openFolder: (path) => void bridge.openFolder(path),
    showInFolder: (path) => void bridge.showInFolder(path),
    themeChanged: (mode) => bridge.setTheme(mode),
  };
}
