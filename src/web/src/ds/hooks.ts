import { useEffect, useRef, useState, type RefObject } from "react";

/** The latest value in a ref, for event handlers and timers that must not restart effects. */
export function useLatest<T>(value: T) {
  const ref = useRef(value);
  useEffect(() => {
    ref.current = value;
  });
  return ref;
}

/** Controlled when `value` is given, otherwise keeps its own state; `onChange` fires either way.
 *  `initial` may be undefined (no items yet), then so may be the current value. */
export function useControlled<T, I extends T | undefined = T>(
  value: T | undefined,
  initial: I,
  onChange?: (value: T) => void,
): [T | I, (value: T) => void] {
  const [own, setOwn] = useState<T | I>(initial);
  const controlled = value !== undefined;
  const set = (next: T) => {
    if (!controlled) setOwn(next);
    onChange?.(next);
  };
  return [controlled ? value : own, set];
}

/** Mouse down outside `ref` while `active`. Esc is the layer stack's job (layers.ts). */
export function useClickOutside(
  ref: RefObject<HTMLElement | null>,
  active: boolean,
  onOutside: () => void,
) {
  const handler = useLatest(onOutside);
  useEffect(() => {
    if (!active) return undefined;
    const onDown = (e: MouseEvent) => {
      if (ref.current && e.target instanceof Node && !ref.current.contains(e.target)) {
        handler.current();
      }
    };
    document.addEventListener("mousedown", onDown);
    return () => document.removeEventListener("mousedown", onDown);
  }, [active, ref, handler]);
}
