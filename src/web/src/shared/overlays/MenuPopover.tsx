import { Menu, type MenuItem } from "../../ds";
import { Popover, type PopoverProps } from "./Popover";

export interface MenuPopoverProps<V extends string> extends Omit<PopoverProps, "children"> {
  items: readonly (MenuItem<V> | "-")[];
  label?: string;
  onSelect: (value: V) => void;
  /** Width and the like of the menu panel */
  menuClassName?: string;
}

/** The «⋯» menu: a Menu in a popover. Picking an item closes it first, so an item that opens
 *  a window or a confirmation starts from a clean layer stack. */
export function MenuPopover<V extends string>({
  items,
  label,
  onSelect,
  menuClassName,
  ...popover
}: MenuPopoverProps<V>) {
  return (
    <Popover {...popover}>
      <Menu
        items={items}
        label={label}
        className={menuClassName}
        autoFocus
        onSelect={(value) => {
          popover.onClose();
          onSelect(value);
        }}
      />
    </Popover>
  );
}
