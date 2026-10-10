import type { StringKey } from "../strings/ru";

/* One registry of hotkeys (R16). Screens bind handlers with useHotkey; the «Клавиши» list in
   Settings is built from HOTKEYS, so a new key shows up there by itself. Keys match by
   KeyboardEvent.code, so I and O work on the Russian layout too. */

export type HotkeyScope = "editor" | "projects";

export interface Hotkey {
  /** KeyboardEvent.code */
  code: string;
  ctrl?: boolean;
  shift?: boolean;
  /** What Kbd shows */
  keys: readonly string[];
  scope: HotkeyScope;
  label: StringKey;
  /** Taken for later: nothing binds it yet and Settings does not list it */
  reserved?: boolean;
}

export const HOTKEYS = {
  playPause: { code: "Space", keys: ["Пробел"], scope: "editor", label: "hotkey.playPause" },
  prevFrame: { code: "ArrowLeft", keys: ["←"], scope: "editor", label: "hotkey.prevFrame" },
  nextFrame: { code: "ArrowRight", keys: ["→"], scope: "editor", label: "hotkey.nextFrame" },
  markIn: { code: "KeyI", keys: ["I"], scope: "editor", label: "hotkey.markIn" },
  markOut: { code: "KeyO", keys: ["O"], scope: "editor", label: "hotkey.markOut" },
  undo: { code: "KeyZ", ctrl: true, keys: ["Ctrl", "Z"], scope: "editor", label: "hotkey.undo" },
  redo: {
    code: "KeyZ",
    ctrl: true,
    shift: true,
    keys: ["Ctrl", "Shift", "Z"],
    scope: "editor",
    label: "hotkey.redo",
  },
  rename: { code: "F2", keys: ["F2"], scope: "projects", label: "hotkey.rename" },
  delete: {
    code: "Delete",
    keys: ["Del"],
    scope: "projects",
    label: "hotkey.delete",
    reserved: true,
  },
} as const satisfies Record<string, Hotkey>;

export type HotkeyId = keyof typeof HOTKEYS;

type KeyLike = Pick<KeyboardEvent, "code" | "ctrlKey" | "metaKey" | "shiftKey" | "altKey">;

/** Exact match: Ctrl+Shift+Z is not Ctrl+Z. Cmd counts as Ctrl. */
export function matches(hotkey: Hotkey, e: KeyLike): boolean {
  const ctrl = e.ctrlKey || e.metaKey;
  return (
    e.code === hotkey.code &&
    ctrl === Boolean(hotkey.ctrl) &&
    e.shiftKey === Boolean(hotkey.shift) &&
    !e.altKey
  );
}
