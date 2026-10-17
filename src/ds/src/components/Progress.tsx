import { cx } from "../cx";

export interface ProgressProps {
  /** 0–100 */
  value: number;
  /** What exactly is happening: «Кодирую в VP9», «Отправляем в Telegram» */
  label?: string;
  showValue?: boolean;
  /** rainbow: only for sending stickers to the pack */
  variant?: "default" | "rainbow";
  className?: string;
}

export function Progress({
  value,
  label,
  showValue = true,
  variant = "default",
  className,
}: ProgressProps) {
  const v = Math.max(0, Math.min(100, value));
  return (
    <div className={cx("ss-progress", variant === "rainbow" && "ss-progress-rainbow", className)}>
      <div className="ss-progress-row">
        <div
          className="ss-progress-track"
          role="progressbar"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={Math.round(v)}
          aria-label={label ?? "Прогресс"}
        >
          <div className="ss-progress-fill" style={{ width: `${v}%` }} />
        </div>
        {showValue && <span className="ss-progress-value">{Math.round(v)}%</span>}
      </div>
      {label && <div className="ss-progress-label">{label}</div>}
    </div>
  );
}
