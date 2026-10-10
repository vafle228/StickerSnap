import { describe, expect, it } from "vitest";
import { HOTKEYS } from "../../shared/hotkeys";
import { hotkeysByScope, versionsLine } from "./model";
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
  it("joins the versions in one line", () => {
    const app = {
      appVersion: "0.1.0",
      ffmpegVersion: "7.1",
      apiVersion: "1.0.0",
      mode: "web" as const,
      botUsername: "StickerSnapAppBot",
      limits: {
        sticker: { maxBytes: 262144, side: 512, maxDurationSec: 3, maxFps: 30 },
        emoji: { maxBytes: 65536, side: 100, maxDurationSec: 3, maxFps: 30 },
        packCapacity: { regular: 120, custom_emoji: 200 },
      },
    };
    expect(versionsLine(app, "Windows")).toBe(
      "StickerSnap 0.1.0 · FFmpeg 7.1 · API 1.0.0 · web · Windows",
    );
  });
});
