import { useEffect, useRef, useState, type RefObject } from "react";

/** The latest value in a ref, for event handlers and timers that must not restart effects. */
export function useLatest<T>(value: T) {
  const ref = useRef(value);
  useEffect(() => {
    ref.current = value;
  });
  return ref;
}

/** Controlled when `value` is given, otherwise keeps its own state; `onChange` fires either way. */
export function useControlled<T>(
  value: T | undefined,
  initial: T,
  onChange?: (value: T) => void,
): [T, (value: T) => void] {
  const [own, setOwn] = useState(initial);
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
