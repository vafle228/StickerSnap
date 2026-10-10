import {
  autoUpdate,
  flip,
  offset,
  shift,
  useFloating,
  type Placement,
} from "@floating-ui/react-dom";
import { useEffect, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { useLayer } from "../../ds";
import { useLatest } from "../../ds/hooks";
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
  const { refs, floatingStyles } = useFloating({
    open,
    placement,
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

  if (!open) return null;
  return createPortal(
    <div ref={refs.setFloating} className={`ss-root ${styles.popover}`} style={floatingStyles}>
      {children}
    </div>,
    document.body,
  );
}
