import { describe, expect, it } from "vitest";
import { HOTKEYS, matches } from "./registry";

const press = (code: string, mods: { ctrl?: boolean; shift?: boolean; alt?: boolean } = {}) => ({
  code,
  ctrlKey: Boolean(mods.ctrl),
  metaKey: false,
  shiftKey: Boolean(mods.shift),
  altKey: Boolean(mods.alt),
});

describe("hotkeys", () => {
  it("tells Ctrl+Z from Ctrl+Shift+Z", () => {
    expect(matches(HOTKEYS.undo, press("KeyZ", { ctrl: true }))).toBe(true);
    expect(matches(HOTKEYS.undo, press("KeyZ", { ctrl: true, shift: true }))).toBe(false);
    expect(matches(HOTKEYS.redo, press("KeyZ", { ctrl: true, shift: true }))).toBe(true);
  });

  it("matches by key position, so I works on the Russian layout", () => {
    expect(matches(HOTKEYS.markIn, press("KeyI"))).toBe(true);
    expect(matches(HOTKEYS.markIn, press("KeyI", { ctrl: true }))).toBe(false);
  });

  it("keeps every key of R16 in one registry without clashes", () => {
    const combos = Object.values(HOTKEYS).map(
      (h) => `${"ctrl" in h ? "C" : ""}${"shift" in h ? "S" : ""}${h.code}`,
    );
    expect(new Set(combos).size).toBe(combos.length);
  });
});
