import { useEffect } from "react";
import { useLatest } from "./hooks";

/* Overlay layers (artboard «12 · Окна и попапы»):
   1 popover/menu · 2 window · 3 confirmation. One Esc closes the top layer only,
   one popover is open at a time, and opening a window closes the open popovers.
   Toasts are not layers: Esc never touches them. */

export type LayerKind = "popover" | "dialog" | "confirm";

interface Layer {
  kind: LayerKind;
  close: () => void;
}

const stack: Layer[] = [];

function remove(layer: Layer) {
  const i = stack.indexOf(layer);
  if (i >= 0) stack.splice(i, 1);
}

function onKeyDown(e: KeyboardEvent) {
  if (e.key !== "Escape") return;
  const top = stack[stack.length - 1];
  if (!top) return;
  e.preventDefault();
  e.stopPropagation();
  top.close();
}

/** Registers an open overlay; `close` runs on Esc when it is the top layer, or when a newer layer evicts it. */
export function useLayer(open: boolean, kind: LayerKind, close: () => void) {
  const latest = useLatest(close);
  useEffect(() => {
    if (!open) return undefined;
    const layer: Layer = { kind, close: () => latest.current() };
    if (kind !== "confirm") {
      for (const other of stack.filter((l) => l.kind === "popover")) {
        remove(other);
        other.close();
      }
    }
    if (stack.length === 0) document.addEventListener("keydown", onKeyDown, true);
    stack.push(layer);
    return () => {
      remove(layer);
      if (stack.length === 0) document.removeEventListener("keydown", onKeyDown, true);
    };
  }, [open, kind, latest]);
}
