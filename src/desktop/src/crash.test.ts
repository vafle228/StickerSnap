import { describe, expect, it } from "vitest";
import { beginLaunch, OUTPUT_TAIL, serverCrash, windowCrash } from "./crash.js";
import type { ShellState } from "./state.js";

const NOW = new Date("2026-10-19T13:22:00Z");

describe("the crash of the last launch", () => {
  it("is none after a clean quit, and marks this launch running", () => {
    const state: ShellState = { theme: "system", running: false };
    expect(beginLaunch(state, NOW)).toBeUndefined();
    expect(state).toEqual({ theme: "system", running: true, startedAt: NOW.toISOString() });
  });

  it("is the shell's when the last launch never quit", () => {
    const state: ShellState = { theme: "system", running: true, startedAt: "2026-10-19T10:00:00Z" };
    expect(beginLaunch(state, NOW)).toEqual({ kind: "shell", at: "2026-10-19T10:00:00Z" });
  });

  it("is what the last launch wrote down, handed over once", () => {
    const crash = serverCrash(3, "Unhandled exception", NOW);
    const state: ShellState = { theme: "dark", running: false, crash };
    expect(beginLaunch(state, NOW)).toEqual(crash);
    expect(state.crash).toBeUndefined();
    state.running = false;
    expect(beginLaunch(state, NOW)).toBeUndefined();
  });

  it("keeps the server's code and the end of its output", () => {
    const output = `${"a".repeat(OUTPUT_TAIL)}Unhandled exception`;
    const crash = serverCrash(3, output, NOW);
    expect(crash).toMatchObject({ kind: "server", at: NOW.toISOString(), code: 3 });
    expect(crash.output).toHaveLength(OUTPUT_TAIL);
    expect(crash.output?.endsWith("Unhandled exception")).toBe(true);
  });

  it("names why the window's renderer died", () => {
    expect(windowCrash("oom", NOW)).toEqual({
      kind: "window",
      at: NOW.toISOString(),
      reason: "oom",
    });
  });
});
