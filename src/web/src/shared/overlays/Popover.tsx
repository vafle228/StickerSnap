import {
  autoUpdate,
  flip,
  offset,
  shift,
  useFloating,
  type Placement,
} from "@floating-ui/react-dom";
import { useEffect, type CSSProperties, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { useLayer, usePresence } from "@stickersnap/ds";
import { useLatest } from "@stickersnap/ds/hooks";
import { originOf } from "./origin";
import styles from "./Popover.module.css";

export interface PopoverProps {
  /** The button it hangs from; a click on it is not a click outside */
  anchor: HTMLElement | null;
  open: boolean;
  onClose: () => void;
  placement?: Placement;
  children: ReactNode;
}

/** Layer 1 (artboard 12): a menu or a small panel next to its button. Only one is open at a time,
 *  a window closes it, Esc and a click outside close it. Placed by Floating UI, so it flips and
 *  shifts to stay inside the window. */
export function Popover({
  anchor,
  open,
  onClose,
  placement = "bottom-end",
  children,
}: PopoverProps) {
  const presence = usePresence(open);
  // top/left instead of a transform: the transform is the entry's scale.
  const {
    refs,
    floatingStyles,
    placement: placed,
  } = useFloating({
    open: presence.mounted,
    placement,
    transform: false,
    elements: { reference: anchor },
    middleware: [offset(6), flip({ padding: 8 }), shift({ padding: 8 })],
    whileElementsMounted: autoUpdate,
  });
  const close = useLatest(onClose);

  // Esc hands the focus back to the button; a click elsewhere leaves it where the click put it.
  useLayer(open, "popover", () => {
    onClose();
    anchor?.focus();
  });

  useEffect(() => {
    if (!open) return undefined;
    const onDown = (e: MouseEvent) => {
      const target = e.target;
      if (!(target instanceof Node)) return;
      if (refs.floating.current?.contains(target) || anchor?.contains(target)) return;
      close.current();
    };
    document.addEventListener("mousedown", onDown);
    return () => document.removeEventListener("mousedown", onDown);
  }, [open, anchor, refs.floating, close]);

  if (!presence.mounted) return null;
  return createPortal(
    <div
      ref={(el) => {
        refs.setFloating(el);
        presence.ref(el);
      }}
      className={`ss-root ss-pop ${styles.popover}`}
      data-state={presence.state}
      style={{ ...floatingStyles, "--ss-origin": originOf(placed) } as CSSProperties}
    >
      {children}
    </div>,
    document.body,
  );
}
