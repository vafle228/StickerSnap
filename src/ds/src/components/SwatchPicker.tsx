import { cx } from "../cx";
import { ACCENTS, type AccentName, type Swatch } from "../accents";
import { useControlled } from "../hooks";

export type { AccentName, Swatch };

export interface SwatchPickerProps<V extends string = AccentName> {
  /** Defaults to the five accents */
  items?: readonly Swatch<V>[];
  value?: V;
  defaultValue?: V;
  onChange?: (value: V) => void;
  label?: string;
  showLabel?: boolean;
  className?: string;
}

export function SwatchPicker<V extends string = AccentName>({
  items = ACCENTS as unknown as readonly Swatch<V>[],
  value,
  defaultValue,
  onChange,
  label = "Акцент",
  showLabel = true,
  className,
}: SwatchPickerProps<V>) {
  const [current, setCurrent] = useControlled(value, defaultValue ?? items[0]?.value, onChange);
  const chosen = items.find((it) => it.value === current);
  return (
    <div className={className}>
      <div className="ss-swatches" role="radiogroup" aria-label={label}>
        {items.map((it) => {
          const on = it.value === current;
          return (
            <button
              key={it.value}
              type="button"
              role="radio"
              aria-checked={on}
              aria-label={it.label}
              title={it.label}
              className={cx("ss-swatch", on && "is-on")}
              style={{ background: it.color }}
              onClick={() => setCurrent(it.value)}
            />
          );
        })}
      </div>
      {showLabel && chosen && <span className="ss-swatch-label">{chosen.label}</span>}
    </div>
  );
}
