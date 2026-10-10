import { useEffect } from "react";
import { hasModalLayer } from "../../ds";
import { useLatest } from "../../ds/hooks";
import { HOTKEYS, matches, type Hotkey, type HotkeyId } from "./registry";

type Handler = (e: KeyboardEvent) => void;

const bound = new Map<HotkeyId, Handler[]>();

/** Typing goes to the field: Space, arrows and letters there are text, not commands. */
function isTyping(target: EventTarget | null) {
  if (!(target instanceof HTMLElement)) return false;
  return (
    target.isContentEditable ||
    target instanceof HTMLTextAreaElement ||
    target instanceof HTMLSelectElement ||
    (target instanceof HTMLInputElement &&
      !["checkbox", "radio", "range", "button"].includes(target.type))
  );
}

/** Which bound hotkey a key press means, if any; the last bound handler wins. */
function resolve(e: KeyboardEvent): Handler | undefined {
  if (e.repeat && e.code !== "ArrowLeft" && e.code !== "ArrowRight") return undefined;
  // While a window or a confirmation is open, the screen under it stays silent (R16).
  if (hasModalLayer() || isTyping(e.target)) return undefined;
  for (const [id, handlers] of bound) {
    const hotkey: Hotkey = HOTKEYS[id];
    if (!hotkey.reserved && matches(hotkey, e)) return handlers[handlers.length - 1];
  }
  return undefined;
}

function onKeyDown(e: KeyboardEvent) {
  const handler = resolve(e);
  if (!handler) return;
  e.preventDefault();
  handler(e);
}

/** Binds a hotkey from the registry while the component is mounted (and `enabled`). */
export function useHotkey(id: HotkeyId, handler: Handler, enabled = true) {
  const latest = useLatest(handler);
  useEffect(() => {
    if (!enabled) return undefined;
    const run: Handler = (e) => latest.current(e);
    if (bound.size === 0) window.addEventListener("keydown", onKeyDown);
    bound.set(id, [...(bound.get(id) ?? []), run]);
    return () => {
      const rest = (bound.get(id) ?? []).filter((h) => h !== run);
      if (rest.length > 0) bound.set(id, rest);
      else bound.delete(id);
      if (bound.size === 0) window.removeEventListener("keydown", onKeyDown);
    };
  }, [id, enabled, latest]);
}
