import { cx } from "../cx";
import { prefersReducedMotion, useControlled } from "../hooks";
import { itemsOf } from "../types";
import type { TabsProps } from "./Tabs";

/** Item value = id of the section element it scrolls to */
export interface SectionNavProps<V extends string = string> extends TabsProps<V> {
  label?: string;
}

/** Table of contents of a long screen (Настройки): smooth scroll to the section, instant when
 *  the system asks for less motion (CSS cannot reach a scroll started from JS). */
export function SectionNav<V extends string = string>({
  items,
  value,
  defaultValue,
  onChange,
  label = "Разделы",
  className,
}: SectionNavProps<V>) {
  const list = itemsOf(items);
  const [current, setCurrent] = useControlled(value, defaultValue ?? list[0]?.value, onChange);
  return (
    <nav className={cx("ss-secnav", className)} aria-label={label}>
      {list.map((it) => {
        const on = it.value === current;
        return (
          <a
            key={it.value}
            href={`#${it.value}`}
            aria-current={on ? "true" : undefined}
            className={on ? "is-active" : undefined}
            onClick={(e) => {
              e.preventDefault();
              setCurrent(it.value);
              document.getElementById(it.value)?.scrollIntoView({
                behavior: prefersReducedMotion() ? "auto" : "smooth",
                block: "start",
              });
            }}
          >
            {it.label}
          </a>
        );
      })}
    </nav>
  );
}
