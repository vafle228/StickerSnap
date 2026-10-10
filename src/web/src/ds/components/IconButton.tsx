import type { ButtonHTMLAttributes } from "react";
import { cx } from "../cx";
import { Icon, type IconName } from "./Icon";

export interface IconButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  icon: IconName;
  /** Required: aria-label and tooltip */
  label: string;
  /** Selected tool: primary-soft, relief removed */
  active?: boolean;
  /** Highlight (peach) dot: something new */
  dot?: boolean;
  /** No relief: inside cards, popovers and menus */
  flat?: boolean;
  /** md 40px · sm 32px */
  size?: "md" | "sm";
}

export function IconButton({
  icon,
  label,
  active,
  dot,
  flat,
  size = "md",
  className,
  ...rest
}: IconButtonProps) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      aria-pressed={active}
      {...rest}
      className={cx(
        "ss-iconbtn",
        active && "is-active",
        flat && "ss-iconbtn-flat",
        size === "sm" && "ss-iconbtn-sm",
        className,
      )}
    >
      <Icon name={icon} size={size === "sm" ? 16 : 20} />
      {dot && <span className="ss-dot" aria-hidden />}
    </button>
  );
}
