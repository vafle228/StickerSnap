import type { CSSProperties } from "react";
import { cx } from "../cx";
import { useControlled } from "../hooks";

export interface SliderProps {
  label?: string;
  min?: number;
  max?: number;
  step?: number;
  value?: number;
  defaultValue?: number;
  onChange?: (value: number) => void;
  /** Units next to the value: «1,25×», «24 fps» */
  format?: (value: number) => string;
  className?: string;
}

export function Slider({
  label,
  min = 0,
  max = 100,
  step = 1,
  value,
  defaultValue,
  onChange,
  format = String,
  className,
}: SliderProps) {
  const [current, setCurrent] = useControlled(value, defaultValue ?? min, onChange);
  const fill = ((current - min) / (max - min)) * 100;
  return (
    <div className={cx("ss-slider", className)}>
      {label && (
        <div className="ss-slider-head">
          <span>{label}</span>
          <b>{format(current)}</b>
        </div>
      )}
      <input
        type="range"
        min={min}
        max={max}
        step={step}
        value={current}
        aria-label={label}
        aria-valuetext={format(current)}
        style={{ "--ss-fill": `${fill}%` } as CSSProperties}
        onChange={(e) => setCurrent(Number(e.target.value))}
      />
    </div>
  );
}
