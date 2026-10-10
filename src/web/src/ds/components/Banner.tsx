import type { ReactNode } from "react";
import { cx } from "../cx";
import { TONE_ICON, type Tone } from "../types";
import { Icon, type IconName } from "./Icon";
import { IconButton } from "./IconButton";

export interface BannerProps {
  tone?: Tone;
  title: ReactNode;
  description?: ReactNode;
  icon?: IconName;
  /** One action that fixes the problem («Указать файл…») */
  action?: ReactNode;
  /** Only info banners can be closed */
  onClose?: () => void;
  className?: string;
}

/** A lasting problem shown on top of what it belongs to, until it is fixed (artboard 8). */
export function Banner({
  tone = "warning",
  title,
  description,
  icon,
  action,
  onClose,
  className,
}: BannerProps) {
  return (
    <div
      className={cx("ss-banner", `ss-tone-${tone}`, className)}
      role={tone === "danger" || tone === "warning" ? "alert" : "status"}
    >
      <div className="ss-tone-icon" aria-hidden>
        <Icon name={icon ?? TONE_ICON[tone]} size={18} strokeWidth={2} />
      </div>
      <div className="ss-banner-body">
        <p className="ss-banner-title">{title}</p>
        {description && <p className="ss-banner-desc">{description}</p>}
      </div>
      {action}
      {tone === "info" && onClose && (
        <IconButton icon="close" label="Закрыть" flat size="sm" onClick={onClose} />
      )}
    </div>
  );
}
