import { useId, type ButtonHTMLAttributes, type ReactNode } from "react";
import { cx } from "../cx";
import { Icon, type IconName } from "./Icon";

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  /** primary: one per screen, follows the accent · secondary: default · ghost: tertiary ·
   *  danger: irreversible verb in ConfirmDialog only · prism: «мягкая радуга», «Опубликовать в Telegram» only, one per screen */
  variant?: "primary" | "secondary" | "ghost" | "danger" | "prism";
  /** md 40px · sm 32px */
  size?: "md" | "sm";
  icon?: IconName;
  iconRight?: IconName;
  /** Shown under a disabled button: why it is off («Нужен исходник») */
  reason?: string;
  children?: ReactNode;
}

export function Button({
  variant = "secondary",
  size = "md",
  icon,
  iconRight,
  reason,
  className,
  children,
  ...rest
}: ButtonProps) {
  const reasonId = useId();
  const why = rest.disabled ? reason : undefined;
  const iconSize = size === "sm" ? 16 : 18;
  const button = (
    <button
      type="button"
      aria-describedby={why ? reasonId : undefined}
      {...rest}
      className={cx("ss-btn", `ss-btn-${variant}`, size === "sm" && "ss-btn-sm", className)}
    >
      {icon && <Icon name={icon} size={iconSize} prism={variant === "prism" && !rest.disabled} />}
      {children}
      {iconRight && <Icon name={iconRight} size={iconSize} />}
    </button>
  );
  if (!why) return button;
  return (
    <span className="ss-btn-wrap">
      {button}
      <span className="ss-btn-reason" id={reasonId}>
        {why}
      </span>
    </span>
  );
}
