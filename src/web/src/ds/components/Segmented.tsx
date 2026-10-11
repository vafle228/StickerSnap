import { cx } from "../cx";
import { useControlled, useSlidingThumb } from "../hooks";
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
  const { group, box, ready } = useSlidingThumb(current);
  return (
    <div
      ref={group}
      className={cx("ss-seg", box && "has-thumb", className)}
      role="radiogroup"
      aria-label={label}
    >
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
      {box && (
        <span
          className={cx("ss-seg-thumb", ready && "is-ready")}
          style={{ width: box.width, transform: `translateX(${box.x}px)` }}
          aria-hidden
        />
      )}
    </div>
  );
}
