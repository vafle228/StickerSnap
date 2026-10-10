import { Fragment, type ReactNode } from "react";
import { cx } from "../cx";

export interface KbdProps {
  /** A combination: keys joined by «+» («Ctrl», «Shift», «Z») */
  keys?: ReactNode[];
  children?: ReactNode;
  className?: string;
}

export function Kbd({ keys, children, className }: KbdProps) {
  const list = keys ?? (children != null ? [children] : []);
  if (list.length === 1) return <kbd className={cx("ss-kbd", className)}>{list[0]}</kbd>;
  return (
    <span className={cx("ss-kbds", className)}>
      {list.map((key, i) => (
        <Fragment key={i}>
          {i > 0 && <span aria-hidden>+</span>}
          <kbd className="ss-kbd">{key}</kbd>
        </Fragment>
      ))}
    </span>
  );
}
