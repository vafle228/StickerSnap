import type { ReactNode } from "react";
import { cx } from "../cx";
import type { Tone } from "../types";
import { Icon, type IconName } from "./Icon";

export interface BadgeProps {
  /** Tones always come with an icon or a word. highlight: only «новое», one per card */
  tone?: "neutral" | "primary" | "highlight" | Tone;
  icon?: IconName;
  /** Process in progress: spinner instead of the icon («отправляем…») */
  busy?: boolean;
  /** Makes the badge a button that leads to the fix. A badge cannot be closed */
  onClick?: () => void;
  "aria-haspopup"?: "dialog" | "menu";
  "aria-expanded"?: boolean;
  children?: ReactNode;
  className?: string;
}

/** 24px pill that keeps a state in view while it lasts (artboard 8). */
export function Badge({
  tone = "neutral",
  icon,
  busy,
  onClick,
  children,
  className,
  ...aria
}: BadgeProps) {
  const name = busy ? "spinner" : icon;
  const content = (
    <>
      {name && <Icon name={name} size={14} strokeWidth={2.25} />}
      {children}
    </>
  );
  const classes = cx("ss-badge", `ss-badge-${tone}`, className);
  if (!onClick) return <span className={classes}>{content}</span>;
  return (
    <button type="button" className={classes} onClick={onClick} {...aria}>
      {content}
    </button>
  );
}
