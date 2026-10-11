import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { readState, writeState } from "./state.js";

const file = () => join(mkdtempSync(join(tmpdir(), "stickersnap-state-")), "state.json");

describe("the shell's memory between launches", () => {
  it("starts with the system theme when nothing is saved", () => {
    expect(readState(file())).toEqual({ theme: "system" });
  });

  it("keeps the port, the theme and the window", () => {
    const path = file();
    const state = {
      port: 52_000,
      theme: "dark" as const,
      bounds: { x: 10, y: 20, width: 1300, height: 800 },
      maximized: true,
    };
    writeState(path, state);
    expect(readState(path)).toEqual(state);
  });

  it("drops what is broken and keeps the rest", () => {
    const path = file();
    const broken = { port: 70_000, theme: "pink", bounds: { x: 1 }, maximized: false };
    writeFileSync(path, JSON.stringify(broken));
    expect(readState(path)).toEqual({ theme: "system", maximized: false });
    writeFileSync(path, "{ half a file");
    expect(readState(path)).toEqual({ theme: "system" });
  });
});
