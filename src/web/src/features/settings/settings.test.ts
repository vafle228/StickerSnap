import { describe, expect, it } from "vitest";
import { HOTKEYS } from "../../shared/hotkeys";
import { ffmpegText, hotkeysByScope, versionsLine } from "./model";
import { sectionAt } from "./useActiveSection";

describe("sectionAt", () => {
  it("picks the last section whose top passed the line", () => {
    expect(sectionAt([0, 400, 900], false)).toBe(0);
    expect(sectionAt([-500, 40, 600], false)).toBe(1);
    expect(sectionAt([-900, -400, 96], false)).toBe(2);
  });

  it("picks the last section at the end of the scroll", () => {
    expect(sectionAt([-900, -400, 300], true)).toBe(2);
  });
});

describe("hotkeysByScope", () => {
  it("lists every key but the reserved ones, editor first", () => {
    const groups = hotkeysByScope();
    expect(groups.map((g) => g.scope)).toEqual(["editor", "projects"]);
    const listed = groups.flatMap((g) => g.keys);
    expect(listed).toContain(HOTKEYS.playPause);
    expect(listed).toContain(HOTKEYS.rename);
    expect(listed).not.toContain(HOTKEYS.delete);
  });
});

describe("versionsLine", () => {
  const app = {
    appVersion: "0.1.0",
    ffmpeg: { status: "ok" as const, version: "9.0.2" },
    apiVersion: "1.0.0",
    mode: "web" as const,
    botUsername: "StickerSnapAppBot",
    limits: {
      sticker: {
        maxBytes: 262144,
        side: 512,
        maxDurationSec: 3,
        longMaxDurationSec: 10,
        maxFps: 30,
      },
      emoji: { maxBytes: 262144, side: 100, maxDurationSec: 3, longMaxDurationSec: 10, maxFps: 30 },
      packCapacity: { regular: 120, custom_emoji: 200 },
    },
  };

  it("joins the versions in one line", () => {
    expect(versionsLine(app, "Windows")).toBe(
      "StickerSnap 0.1.0 · FFmpeg 9.0.2 · API 1.0.0 · web · Windows",
    );
  });

  it("says why FFmpeg cannot be used", () => {
    const old = { ...app, ffmpeg: { status: "too_old" as const, version: "7.1" } };
    const missing = { ...app, ffmpeg: { status: "missing" as const } };
    expect(versionsLine(old, "Linux")).toContain("· FFmpeg 7.1 too_old ·");
    expect(versionsLine(missing, "Linux")).toContain("· FFmpeg missing ·");
  });
});

describe("ffmpegText", () => {
  it("shows the version, or why there is no FFmpeg to render with", () => {
    expect(ffmpegText({ status: "ok", version: "9.0.2" })).toBe("9.0.2");
    expect(ffmpegText({ status: "too_old", version: "7.1" })).toBe("7.1, нужна 9.0 или новее");
    expect(ffmpegText({ status: "missing" })).toBe("не найден");
  });
});
