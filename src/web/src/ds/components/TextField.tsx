import { useId, type InputHTMLAttributes, type ReactNode } from "react";
import { cx } from "../cx";
import { Icon, type IconName } from "./Icon";

export interface TextFieldProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: string;
  hint?: string;
  /** Replaces hint, turns the ring danger; the form cannot be sent */
  error?: string;
  /** Replaces hint in warning tone; the form can still be sent */
  warning?: string;
  /** One fix shown after the message: { label: "Взять kot_divan_2", onClick } */
  fix?: { label: string; onClick: () => void };
  icon?: IconName;
  /** Fixed tail the user cannot edit, e.g. "_by_StickerSnapAppBot" */
  suffix?: string;
  trailing?: ReactNode;
}

/** Sunken pill field. The hint under it is one of the five notification forms (artboard 8). */
export function TextField({
  label,
  hint,
  error,
  warning,
  fix,
  icon,
  suffix,
  trailing,
  className,
  id,
  ...rest
}: TextFieldProps) {
  const ownId = useId();
  const fieldId = id ?? ownId;
  const hintId = `${fieldId}-hint`;
  const message = error || warning || hint;
  return (
    <div
      className={cx("ss-field", error && "is-error", !error && warning && "is-warning", className)}
    >
      {label && (
        <label className="ss-field-label" htmlFor={fieldId}>
          {label}
        </label>
      )}
      <div className="ss-field-box">
        {icon && <Icon name={icon} size={18} />}
        <input
          id={fieldId}
          aria-invalid={error ? true : undefined}
          aria-describedby={message ? hintId : undefined}
          {...rest}
        />
        {suffix && (
          <span className="ss-field-suffix" aria-hidden>
            {suffix}
          </span>
        )}
        {trailing}
      </div>
      {message && (
        <div className="ss-field-hint" id={hintId} aria-live="polite">
          {(error || warning) && (
            <Icon name={error ? "error-circle" : "alert"} size={14} strokeWidth={2} />
          )}
          <span>{message}</span>
          {fix && (
            <button type="button" className="ss-field-fix" onClick={fix.onClick}>
              {fix.label}
            </button>
          )}
        </div>
      )}
    </div>
  );
}
