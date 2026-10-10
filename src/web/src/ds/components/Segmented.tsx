import { cx } from "../cx";
import { useControlled } from "../hooks";
import { itemsOf } from "../types";
import type { TabsProps } from "./Tabs";

export interface SegmentedProps<V extends string = string> extends TabsProps<V> {
  /** Required: the group name for screen readers */
  label: string;
}

/** Two to four short modes: format, scale, theme. */
export function Segmented<V extends string = string>({
  items,
  value,
  defaultValue,
  onChange,
  label,
  className,
}: SegmentedProps<V>) {
  const list = itemsOf(items);
  const [current, setCurrent] = useControlled(value, defaultValue ?? list[0]?.value, onChange);
  return (
    <div className={cx("ss-seg", className)} role="radiogroup" aria-label={label}>
      {list.map((it) => {
        const on = it.value === current;
        return (
          <button
            key={it.value}
            type="button"
            role="radio"
            aria-checked={on}
            className={on ? "is-active" : undefined}
            onClick={() => setCurrent(it.value)}
          >
            {it.label}
          </button>
        );
      })}
    </div>
  );
}
