import { useId } from "react";
import { cx } from "../cx";
import { ICON_PATHS, type IconName } from "../icons/paths";
import { PRISM_STOPS } from "../prism";

export type { IconName };

export interface IconProps {
  name: IconName;
  size?: number;
  label?: string;
  strokeWidth?: number;
  className?: string;
  /** Rainbow stroke — only for the icon of the «Опубликовать в Telegram» button */
  prism?: boolean;
  /** Rotate (the "spinner" icon spins by itself) */
  spin?: boolean;
}

export function Icon({ name, size = 20, label, strokeWidth, className, prism, spin }: IconProps) {
  const gradientId = useId();
  return (
    <svg
      className={cx("ss-icon", (spin || name === "spinner") && "ss-spin", className)}
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke={prism ? `url(#${gradientId})` : "currentColor"}
      strokeWidth={strokeWidth ?? (name === "more" ? 2.75 : 1.75)}
      strokeLinecap="round"
      strokeLinejoin="round"
      role={label ? "img" : undefined}
      aria-label={label}
      aria-hidden={label ? undefined : true}
    >
      {prism && (
        <defs>
          <linearGradient
            id={gradientId}
            x1="2"
            y1="2"
            x2="22"
            y2="22"
            gradientUnits="userSpaceOnUse"
          >
            {PRISM_STOPS.map((color, i) => (
              <stop key={color} offset={i / 3} stopColor={color} />
            ))}
          </linearGradient>
        </defs>
      )}
      {ICON_PATHS[name].map((d) => (
        <path key={d} d={d} />
      ))}
    </svg>
  );
}
