import { cx } from "../cx";
import { useControlled, useSlidingThumb } from "../hooks";
import { itemsOf, type Item } from "../types";

export interface TabsProps<V extends string = string> {
  items: readonly Item<V>[];
  value?: V;
  defaultValue?: V;
  onChange?: (value: V) => void;
  className?: string;
}

/** Underlined tabs for sections of one panel, five at most. */
export function Tabs<V extends string = string>({
  items,
  value,
  defaultValue,
  onChange,
  className,
}: TabsProps<V>) {
  const list = itemsOf(items);
  const [current, setCurrent] = useControlled(value, defaultValue ?? list[0]?.value, onChange);
  const { group, box, ready } = useSlidingThumb(current);
  return (
    <div ref={group} className={cx("ss-tabs", box && "has-thumb", className)} role="tablist">
      {list.map((it) => {
        const on = it.value === current;
        return (
          <button
            key={it.value}
            type="button"
            role="tab"
            aria-selected={on}
            className={cx("ss-tab", on && "is-active")}
            onClick={() => setCurrent(it.value)}
          >
            {it.label}
          </button>
        );
      })}
      {/* The underline sticks out 4px on each side of the label, as the ::after it replaces */}
      {box && (
        <span
          className={cx("ss-tabs-bar", ready && "is-ready")}
          style={{ width: box.width + 8, transform: `translateX(${box.x - 4}px)` }}
          aria-hidden
        />
      )}
    </div>
  );
}
