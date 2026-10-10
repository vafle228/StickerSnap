import type { ReactNode } from "react";
import { cx } from "../cx";
import { useControlled } from "../hooks";
import { Icon } from "./Icon";

export interface CheckboxProps {
  checked?: boolean;
  defaultChecked?: boolean;
  onChange?: (checked: boolean) => void;
  label?: ReactNode;
  disabled?: boolean;
  className?: string;
}

export function Checkbox({
  checked,
  defaultChecked = false,
  onChange,
  label,
  disabled,
  className,
}: CheckboxProps) {
  const [on, setOn] = useControlled(checked, defaultChecked, onChange);
  return (
    <label className={cx("ss-check", className)}>
      <input
        type="checkbox"
        checked={on}
        disabled={disabled}
        onChange={(e) => setOn(e.target.checked)}
      />
      <span className="ss-check-box" aria-hidden>
        <Icon name="check" size={16} strokeWidth={2.5} />
      </span>
      {label && <span>{label}</span>}
    </label>
  );
}
