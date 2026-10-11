import { mkdirSync, readFileSync, renameSync, writeFileSync } from "node:fs";
import { dirname } from "node:path";
import type { ThemeSource } from "./bridge.js";

export interface Bounds {
  x: number;
  y: number;
  width: number;
  height: number;
}

/** What the shell remembers between launches (userData/state.json). */
export interface ShellState {
  /** The server's port of the last launch. The SPA keeps its own state in localStorage, which
   *  belongs to the origin, port included: the same port keeps the open section and the theme. */
  port?: number;
  /** The SPA's theme, so the next window opens in its colour before the SPA paints */
  theme: ThemeSource;
  bounds?: Bounds;
  maximized?: boolean;
}

const DEFAULT: ShellState = { theme: "system" };

const THEMES: readonly ThemeSource[] = ["system", "light", "dark"];

const isRecord = (v: unknown): v is Record<string, unknown> => typeof v === "object" && v !== null;

const isPort = (v: unknown): v is number =>
  typeof v === "number" && Number.isInteger(v) && v > 0 && v < 65536;

function isBounds(v: unknown): v is Bounds {
  return (
    isRecord(v) &&
    ["x", "y", "width", "height"].every((k) => typeof v[k] === "number" && Number.isFinite(v[k]))
  );
}

/** The saved state, or the defaults for whatever is missing or broken: a bad file never stops
 *  the app from starting. */
export function readState(file: string): ShellState {
  let raw: unknown;
  try {
    raw = JSON.parse(readFileSync(file, "utf8"));
  } catch {
    return { ...DEFAULT };
  }
  if (!isRecord(raw)) return { ...DEFAULT };
  const state: ShellState = { ...DEFAULT };
  if (isPort(raw.port)) state.port = raw.port;
  if (THEMES.includes(raw.theme as ThemeSource)) state.theme = raw.theme as ThemeSource;
  if (isBounds(raw.bounds)) state.bounds = raw.bounds;
  if (typeof raw.maximized === "boolean") state.maximized = raw.maximized;
  return state;
}

/** Writes through a temporary file, so a crash mid-write leaves the old state, not half a file. */
export function writeState(file: string, state: ShellState) {
  mkdirSync(dirname(file), { recursive: true });
  const temp = `${file}.tmp`;
  writeFileSync(temp, JSON.stringify(state, null, 2));
  renameSync(temp, file);
}
