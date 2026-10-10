import { useId, useRef, useState } from "react";
import { cx } from "../cx";
import { useClickOutside, useControlled } from "../hooks";
import { useLayer } from "../layers";
import { itemsOf, type Item } from "../types";
import { Icon } from "./Icon";
import { Menu } from "./Menu";

export interface SelectProps<V extends string = string> {
  items: readonly Item<V>[];
  value?: V;
  defaultValue?: V;
  onChange?: (value: V) => void;
  label?: string;
  placeholder?: string;
  id?: string;
  className?: string;
}

/** Five or more options. Two to four are Segmented or RadioGroup. The list is a layer-1 popover. */
export function Select<V extends string = string>({
  items,
  value,
  defaultValue,
  onChange,
  label,
  placeholder = "Выберите",
  id,
  className,
}: SelectProps<V>) {
  const list = itemsOf(items);
  const [current, setCurrent] = useControlled(value, defaultValue, onChange);
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const ownId = useId();
  const selectId = id ?? ownId;
  const labelId = `${selectId}-label`;

  const close = (refocus: boolean) => {
    setOpen(false);
    if (refocus) buttonRef.current?.focus();
  };
  useLayer(open, "popover", () => close(true));
  useClickOutside(rootRef, open, () => close(false));

  const chosen = list.find((it) => it.value === current);
  return (
    <div className={cx("ss-select", className)} ref={rootRef}>
      {label && (
        <span className="ss-field-label" id={labelId}>
          {label}
        </span>
      )}
      <button
        ref={buttonRef}
        id={selectId}
        type="button"
        className="ss-select-btn"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-labelledby={label ? `${labelId} ${selectId}` : undefined}
        onClick={() => setOpen(!open)}
      >
        <span>
          {chosen ? (
            chosen.label
          ) : (
            <span style={{ color: "var(--ink-subtle)" }}>{placeholder}</span>
          )}
        </span>
        <Icon name={open ? "chevron-up" : "chevron-down"} size={18} />
      </button>
      {open && (
        <Menu
          items={list}
          selectable
          autoFocus
          value={current}
          label={label}
          onSelect={(v) => {
            setCurrent(v);
            close(true);
          }}
        />
      )}
    </div>
  );
}
