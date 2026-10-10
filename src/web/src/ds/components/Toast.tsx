import { useEffect, useRef, type ReactNode } from "react";
import { cx } from "../cx";
import { useLatest } from "../hooks";
import { TONE_ICON, type Tone } from "../types";
import { Icon, type IconName } from "./Icon";
import { IconButton } from "./IconButton";
import { Progress } from "./Progress";

export interface ToastProps {
  tone?: Tone;
  /** What happened */
  title: ReactNode;
  /** What to do */
  description?: ReactNode;
  icon?: IconName;
  /** One small Button */
  action?: ReactNode;
  /** Errors only: «Подробности» */
  secondaryAction?: ReactNode;
  /** 0–100: the toast is a running task; it turns into the result in place */
  progress?: number;
  progressLabel?: string;
  /** rainbow: only for sending stickers to the pack */
  progressVariant?: "default" | "rainbow";
  /** ms; default: info/success 6000, 10000 with an action, warning/danger and progress never.
   *  Hover pauses. */
  timeout?: number | null;
  onClose?: () => void;
  className?: string;
}

/** Default lifetime from artboard 8; null = until closed. */
function toastTimeout({
  tone = "info",
  action,
  progress,
  timeout,
}: Pick<ToastProps, "tone" | "action" | "progress" | "timeout">): number | null {
  if (timeout !== undefined) return timeout;
  if (tone === "warning" || tone === "danger" || progress != null) return null;
  return action ? 10000 : 6000;
}

/** Popup: one-off events and results of background tasks. Never takes the focus. */
export function Toast(props: ToastProps) {
  const {
    tone = "info",
    title,
    description,
    icon,
    action,
    secondaryAction,
    progress,
    progressLabel,
    progressVariant,
    onClose,
    className,
  } = props;
  const timeout = toastTimeout(props);
  const paused = useRef(false);
  const close = useLatest(onClose);
  const closable = onClose !== undefined;

  useEffect(() => {
    if (!timeout || !closable) return undefined;
    let left = timeout;
    let last = Date.now();
    const timer = window.setInterval(() => {
      const now = Date.now();
      if (!paused.current) left -= now - last;
      last = now;
      if (left <= 0) {
        window.clearInterval(timer);
        close.current?.();
      }
    }, 250);
    return () => window.clearInterval(timer);
  }, [timeout, closable, close]);

  const running = progress != null;
  return (
    <div
      className={cx("ss-toast", `ss-tone-${tone}`, className)}
      role={tone === "danger" || tone === "warning" ? "alert" : "status"}
      onMouseEnter={() => (paused.current = true)}
      onMouseLeave={() => (paused.current = false)}
    >
      <div className="ss-tone-icon" aria-hidden>
        <Icon name={running ? "spinner" : (icon ?? TONE_ICON[tone])} size={18} strokeWidth={2} />
      </div>
      <div className="ss-toast-body">
        <p className="ss-toast-title">{title}</p>
        {description && <p className="ss-toast-desc">{description}</p>}
        {running && <Progress value={progress} label={progressLabel} variant={progressVariant} />}
        {(action || secondaryAction) && (
          <div className="ss-toast-actions">
            {action}
            {secondaryAction}
          </div>
        )}
      </div>
      {onClose && <IconButton icon="close" label="Закрыть" flat size="sm" onClick={onClose} />}
    </div>
  );
}
