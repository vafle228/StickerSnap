import { useState, type ReactNode } from "react";
import { cx } from "../cx";
import { Icon, type IconName } from "./Icon";

export interface NavRailItem<V extends string = string> {
  value: V;
  label: ReactNode;
  icon?: IconName;
  count?: number;
}

export interface NavRailProps<V extends string = string> {
  items: NavRailItem<V>[];
  /** Pinned to the bottom (Настройки) */
  bottomItems?: NavRailItem<V>[];
  value?: V;
  defaultValue?: V;
  onChange?: (value: V) => void;
  label?: string;
  height?: number | string;
  /** Icons only, 56px wide: the app window look. Labels stay for screen readers and tooltips. */
  narrow?: boolean;
  className?: string;
}

export function NavRail<V extends string = string>({
  items,
  bottomItems = [],
  value,
  defaultValue,
  onChange,
  label = "Разделы приложения",
  height,
  narrow = false,
  className,
}: NavRailProps<V>) {
  const [own, setOwn] = useState(defaultValue ?? items[0]?.value ?? bottomItems[0]?.value);
  const current = value !== undefined ? value : own;

  const renderItem = (item: NavRailItem<V>) => {
    const active = item.value === current;
    return (
      <button
        key={item.value}
        type="button"
        aria-current={active ? "page" : undefined}
        className={cx("ss-rail-item", active && "is-active")}
        title={narrow && typeof item.label === "string" ? item.label : undefined}
        onClick={() => {
          if (value === undefined) setOwn(item.value);
          onChange?.(item.value);
        }}
      >
        {item.icon && <Icon name={item.icon} size={20} />}
        {narrow ? <span className="ss-visually-hidden">{item.label}</span> : item.label}
        {item.count ? <span className="ss-rail-count">{item.count}</span> : null}
      </button>
    );
  };

  return (
    <nav
      className={cx("ss-rail", narrow && "ss-rail-narrow", className)}
      aria-label={label}
      style={height !== undefined ? { height } : undefined}
    >
      {items.map(renderItem)}
      {bottomItems.length > 0 && <div className="ss-rail-spacer" aria-hidden />}
      {bottomItems.map(renderItem)}
    </nav>
  );
}
