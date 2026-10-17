import type { ReactNode } from "react";
import { cx } from "../cx";
import { useControlled } from "../hooks";

export interface RadioItem<V extends string = string> {
  value: V;
  label: ReactNode;
  description?: ReactNode;
  disabled?: boolean;
}

export interface RadioGroupProps<V extends string = string> {
  items: readonly (V | RadioItem<V>)[];
  value?: V;
  defaultValue?: V;
  onChange?: (value: V) => void;
  /** Group name for screen readers */
  label: string;
  columns?: number;
  className?: string;
}

/** Radio cards: one of two to four options with a line of explanation. Short modes are Segmented. */
export function RadioGroup<V extends string = string>({
  items,
  value,
  defaultValue,
  onChange,
  label,
  columns,
  className,
}: RadioGroupProps<V>) {
  const list = items.map((it): RadioItem<V> =>
    typeof it === "string" ? { value: it, label: it } : it,
  );
  const [current, setCurrent] = useControlled(value, defaultValue ?? list[0]?.value, onChange);
  return (
    <div
      className={cx("ss-radios", className)}
      role="radiogroup"
      aria-label={label}
      style={columns ? { gridTemplateColumns: `repeat(${columns}, 1fr)` } : undefined}
    >
      {list.map((it) => {
        const on = it.value === current;
        return (
          <button
            key={it.value}
            type="button"
            role="radio"
            aria-checked={on}
            disabled={it.disabled}
            className={cx("ss-radio", on && "is-on")}
            onClick={() => setCurrent(it.value)}
          >
            <span className="ss-radio-dot" aria-hidden />
            <span>
              <span className="ss-radio-title">{it.label}</span>
              {it.description && <span className="ss-radio-desc">{it.description}</span>}
            </span>
          </button>
        );
      })}
    </div>
  );
}
