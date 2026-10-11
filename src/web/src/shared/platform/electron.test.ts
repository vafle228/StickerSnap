import { afterEach, describe, expect, it, vi } from "vitest";
import type { DesktopBridge } from "@stickersnap/desktop/bridge";
import { serverConnection } from "./connection";
import { electronPlatform, fileName } from "./electron";

const BASE = "http://127.0.0.1:52000";

function fakeBridge(over: Partial<DesktopBridge> = {}): DesktopBridge {
  return {
    server: { baseUrl: BASE, token: "t0k3n" },
    openFile: vi.fn(() => Promise.resolve(null)),
    pickFolder: vi.fn(() => Promise.resolve(null)),
    openFolder: vi.fn(() => Promise.resolve()),
    showInFolder: vi.fn(() => Promise.resolve()),
    openExternal: vi.fn(() => Promise.resolve()),
    saveFile: vi.fn(() => Promise.resolve()),
    setTheme: vi.fn(),
    pathForFile: vi.fn(() => ""),
    ...over,
  };
}

afterEach(() => {
  globalThis.stickersnap = undefined;
});

describe("the SPA in the desktop window", () => {
  it("talks to the server the shell started, with the launch's token", () => {
    globalThis.stickersnap = fakeBridge();
    expect(serverConnection()).toEqual({ baseUrl: BASE, token: "t0k3n" });
  });

  it("stays on its own origin without a token when the shell shows Vite over demo data", () => {
    globalThis.stickersnap = fakeBridge({ server: {} });
    expect(serverConnection().token).toBeUndefined();
  });

  it("imports a picked video by its path, named after the file", async () => {
    const bridge = fakeBridge({ openFile: vi.fn(() => Promise.resolve("C:\\clips\\cat.gif")) });
    const platform = electronPlatform(bridge, BASE);

    const file = await platform.pickVideo();

    expect(bridge.openFile).toHaveBeenCalledWith("video");
    expect(file).toEqual({ name: "cat.gif", path: "C:\\clips\\cat.gif" });
    expect(await platform.sourceOf(file ?? { name: "" })).toEqual({
      kind: "path",
      path: "C:\\clips\\cat.gif",
    });
  });

  it("gives nothing when the dialog is cancelled", async () => {
    const platform = electronPlatform(fakeBridge(), BASE);
    expect(await platform.pickWebm()).toBeNull();
    expect(await platform.pickProject()).toBeNull();
    expect(await platform.pickFolder("/home/me/Stickers")).toBeNull();
  });

  it("takes a dropped file by its path, and uploads one that has none", () => {
    const withPath = fakeBridge({ pathForFile: vi.fn(() => "/home/me/clip.mp4") });
    const file = new File(["x"], "clip.mp4");
    expect(electronPlatform(withPath, BASE).dropped(file)).toEqual({
      name: "clip.mp4",
      path: "/home/me/clip.mp4",
    });
    expect(electronPlatform(fakeBridge(), BASE).dropped(file)).toEqual({ name: "clip.mp4", file });
  });

  it("saves a rendered file from the server's own address", async () => {
    const bridge = fakeBridge();
    await electronPlatform(bridge, BASE).saveFile("/api/v1/files/f1", "Стикер 1.webm");
    expect(bridge.saveFile).toHaveBeenCalledWith(`${BASE}/api/v1/files/f1`, "Стикер 1.webm");
  });

  it("hands folders, links and the theme to the system", () => {
    const bridge = fakeBridge();
    const platform = electronPlatform(bridge, BASE);
    platform.openFolder("/home/me/Stickers");
    platform.showInFolder("/home/me/Stickers/cat.ssproj");
    platform.openExternal("https://t.me/addstickers/cats");
    platform.themeChanged("dark");
    expect(bridge.openFolder).toHaveBeenCalledWith("/home/me/Stickers");
    expect(bridge.showInFolder).toHaveBeenCalledWith("/home/me/Stickers/cat.ssproj");
    expect(bridge.openExternal).toHaveBeenCalledWith("https://t.me/addstickers/cats");
    expect(bridge.setTheme).toHaveBeenCalledWith("dark");
  });

  it("names a file after the last part of its path on Windows and Linux", () => {
    expect(fileName("C:\\Users\\me\\Видео\\кот.mp4")).toBe("кот.mp4");
    expect(fileName("/home/me/кот.mp4")).toBe("кот.mp4");
    expect(fileName("кот.mp4")).toBe("кот.mp4");
  });
});
