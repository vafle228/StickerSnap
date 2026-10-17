import type { HTMLAttributes, ReactNode } from "react";
import { cx } from "../cx";

export interface CardProps extends HTMLAttributes<HTMLElement> {
  /** raised: panels · sunken: frame strip, previews · glass: only over video on the canvas */
  variant?: "raised" | "sunken" | "glass";
  /** Card label in caps */
  eyebrow?: string;
  children?: ReactNode;
}

/** Panel, padding 20, radius-lg. Rows inside are .ss-row; never a raised card in a raised card. */
export function Card({ variant = "raised", eyebrow, className, children, ...rest }: CardProps) {
  return (
    <section
      {...rest}
      className={cx("ss-card", variant !== "raised" && `ss-card-${variant}`, className)}
    >
      {eyebrow && <h3 className="ss-card-eyebrow">{eyebrow}</h3>}
      {children}
    </section>
  );
}
