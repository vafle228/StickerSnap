import type { ReactNode } from "react";
import { cx } from "../cx";
import { useControlled } from "../hooks";

export interface SwitchProps {
  checked?: boolean;
  defaultChecked?: boolean;
  onChange?: (checked: boolean) => void;
  label?: ReactNode;
  disabled?: boolean;
  className?: string;
}

/** Instant setting, no «Сохранить». A choice in a form sent by a button is a Checkbox. */
export function Switch({
  checked,
  defaultChecked = false,
  onChange,
  label,
  disabled,
  className,
}: SwitchProps) {
  const [on, setOn] = useControlled(checked, defaultChecked, onChange);
  return (
    <label className={cx("ss-switch", className)}>
      <input
        type="checkbox"
        role="switch"
        checked={on}
        disabled={disabled}
        onChange={(e) => setOn(e.target.checked)}
      />
      <span className="ss-switch-track" aria-hidden>
        <span className="ss-switch-thumb" />
      </span>
      {label && <span className="ss-switch-label">{label}</span>}
    </label>
  );
}
