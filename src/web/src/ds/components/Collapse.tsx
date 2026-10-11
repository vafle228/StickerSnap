import { useRef, type ReactNode } from "react";
import { cx } from "../cx";
import { usePresence } from "../hooks";

export interface CollapseProps {
  open: boolean;
  children?: ReactNode;
  /** The block was removed and its place has closed */
  onExited?: () => void;
  className?: string;
}

/** Opens and closes its height (a banner, a warning row) instead of pushing the content at once.
 *  Open from the first render means it was already on screen: no entry («Движение в интерфейсе», 5).
 *  While closing it shows what it held last. */
export function Collapse({ open, children, onExited, className }: CollapseProps) {
  const { mounted, state, appear, ref } = usePresence(open, onExited);
  const last = useRef(children);
  if (open) last.current = children;
  if (!mounted) return null;
  return (
    <div
      ref={ref}
      className={cx("ss-collapse", className)}
      data-state={state}
      data-appear={appear ? undefined : "false"}
    >
      <div className="ss-collapse-inner">{open ? children : last.current}</div>
    </div>
  );
}
