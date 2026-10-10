import { useEffect, useRef, type KeyboardEvent, type ReactNode } from "react";
import { cx } from "../cx";
import { Icon, type IconName } from "./Icon";
import { Kbd } from "./Kbd";

export interface MenuItem<V extends string = string> {
  value: V;
  label: ReactNode;
  /** A second, quieter line: what the item leaves untouched («Файл останется на диске») */
  description?: ReactNode;
  icon?: IconName;
  /** «F2» or ["Ctrl", "D"] */
  shortcut?: string | string[];
  /** Irreversible: red, always last after a "-" separator; opens a ConfirmDialog */
  danger?: boolean;
  disabled?: boolean;
}

export interface MenuProps<V extends string = string> {
  items: readonly (MenuItem<V> | "-")[];
  onSelect?: (value: V, item: MenuItem<V>) => void;
  label?: string;
  /** Options list of a Select: a check mark at the chosen item */
  selectable?: boolean;
  value?: V;
  /** Move focus into the menu when it appears (popover menus) */
  autoFocus?: boolean;
  className?: string;
}

const ITEM = "[role^='menuitem']:not([disabled])";

/** Floating panel of the «⋯» menu and of Select. Arrow keys walk the items (artboard 12). */
export function Menu<V extends string = string>({
  items,
  onSelect,
  label,
  selectable,
  value,
  autoFocus,
  className,
}: MenuProps<V>) {
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!autoFocus || !ref.current) return;
    const first =
      ref.current.querySelector<HTMLElement>(`${ITEM}[aria-checked='true']`) ??
      ref.current.querySelector<HTMLElement>(ITEM);
    first?.focus();
  }, [autoFocus]);

  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    const list = Array.from(e.currentTarget.querySelectorAll<HTMLElement>(ITEM));
    if (list.length === 0) return;
    const at = list.indexOf(document.activeElement as HTMLElement);
    const next =
      e.key === "ArrowDown"
        ? list[(at + 1) % list.length]
        : e.key === "ArrowUp"
          ? list[(at - 1 + list.length) % list.length]
          : e.key === "Home"
            ? list[0]
            : e.key === "End"
              ? list[list.length - 1]
              : undefined;
    if (!next) return;
    e.preventDefault();
    next.focus();
  };

  return (
    <div
      ref={ref}
      className={cx("ss-menu", className)}
      role="menu"
      aria-label={label}
      onKeyDown={onKeyDown}
    >
      {items.map((it, i) => {
        if (it === "-") return <div key={`sep-${i}`} className="ss-menu-sep" role="separator" />;
        const chosen = selectable && it.value === value;
        return (
          <button
            key={it.value}
            type="button"
            role={selectable ? "menuitemradio" : "menuitem"}
            aria-checked={selectable ? chosen : undefined}
            disabled={it.disabled}
            className={cx(
              "ss-menu-item",
              it.danger && "is-danger",
              chosen && "is-selected",
              it.description !== undefined && "has-desc",
            )}
            onClick={() => onSelect?.(it.value, it)}
          >
            {it.icon && <Icon name={it.icon} size={18} />}
            {it.description !== undefined ? (
              <span className="ss-menu-label ss-menu-text">
                <span>{it.label}</span>
                <span className="ss-menu-desc">{it.description}</span>
              </span>
            ) : (
              <span className="ss-menu-label">{it.label}</span>
            )}
            {it.shortcut && <Kbd keys={[it.shortcut].flat()} />}
            {chosen && <Icon name="check" size={16} />}
          </button>
        );
      })}
    </div>
  );
}
