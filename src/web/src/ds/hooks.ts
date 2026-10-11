import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type RefCallback,
  type RefObject,
} from "react";

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

/** Longest exit (duration-fast + duration-base of a collapsing toast slot) with room to spare.
 *  Without a transition (jsdom, display: none) the element leaves after this. */
export const EXIT_FALLBACK_MS = 400;

export type PresenceState = "open" | "closed";

export interface Presence {
  /** Render the element while this is true */
  mounted: boolean;
  /** data-state for the CSS: "closed" while the exit transition runs */
  state: PresenceState;
  /** False when the element was open from the first render: it is already on screen, no entry
   *  (principle 5 of docs/ui-motion.md). Put it on the element as data-appear. */
  appear: boolean;
  ref: RefCallback<HTMLElement>;
}

/** Keeps a closing element mounted with data-state="closed" until its exit transition ends.
 *  Entry comes from @starting-style in the CSS. While closing the element is inert: clicks and
 *  focus go past it at once. Ends on transitionend of the element itself (not its children),
 *  or after EXIT_FALLBACK_MS when no transition runs. */
export function usePresence(open: boolean, onExited?: () => void): Presence {
  const [mounted, setMounted] = useState(open);
  const [appear, setAppear] = useState(!open);
  const element = useRef<HTMLElement | null>(null);
  const exited = useLatest(onExited);

  if (open && !mounted) setMounted(true);
  if (!open && !appear) setAppear(true);

  useLayoutEffect(() => {
    const el = element.current;
    if (el) {
      el.inert = !open;
      // Out of the accessibility tree at once, like the focus and the clicks.
      if (open) el.removeAttribute("aria-hidden");
      else el.setAttribute("aria-hidden", "true");
    }
    if (open || !mounted) return undefined;
    let done = false;
    const finish = () => {
      if (done) return;
      done = true;
      setMounted(false);
      exited.current?.();
    };
    const onEnd = (e: TransitionEvent) => {
      if (e.target === el) finish();
    };
    el?.addEventListener("transitionend", onEnd);
    const timer = window.setTimeout(finish, EXIT_FALLBACK_MS);
    return () => {
      el?.removeEventListener("transitionend", onEnd);
      window.clearTimeout(timer);
    };
  }, [open, mounted, exited]);

  const ref = useCallback((el: HTMLElement | null) => {
    element.current = el;
  }, []);

  return { mounted, state: open ? "open" : "closed", appear, ref };
}

/** The system asks for less motion («Эффекты анимации» off in Windows). For JS-driven motion
 *  only: CSS gets the calm values from the tokens. */
export function prefersReducedMotion() {
  return (
    typeof window !== "undefined" &&
    window.matchMedia?.("(prefers-reduced-motion: reduce)").matches === true
  );
}

/** Turns off every transition for two frames, e.g. while the theme repaints the whole window:
 *  otherwise buttons follow the new colors late and the screen smears in patches. */
export function freezeMotion(root: HTMLElement = document.documentElement) {
  root.classList.add("ss-no-motion");
  requestAnimationFrame(() => requestAnimationFrame(() => root.classList.remove("ss-no-motion")));
}

/** One plate per group (Segmented) or one underline (Tabs) that slides to the active item.
 *  Measures the `.is-active` child of `group`, again whenever an item resizes (fonts, labels).
 *  `ready` turns on a frame after the first measure, so the first paint does not slide in. */
export function useSlidingThumb(active: unknown) {
  const group = useRef<HTMLDivElement>(null);
  const [box, setBox] = useState<{ x: number; width: number } | null>(null);
  const [ready, setReady] = useState(false);

  useLayoutEffect(() => {
    const el = group.current;
    if (!el) return undefined;
    const measure = () => {
      const item = el.querySelector<HTMLElement>(":scope > .is-active");
      setBox((prev) => {
        if (!item) return null;
        const next = { x: item.offsetLeft, width: item.offsetWidth };
        return prev?.x === next.x && prev.width === next.width ? prev : next;
      });
    };
    measure();
    if (typeof ResizeObserver === "undefined") return undefined;
    const observer = new ResizeObserver(measure);
    observer.observe(el);
    for (const item of Array.from(el.children)) observer.observe(item);
    return () => observer.disconnect();
  }, [active]);

  useEffect(() => {
    if (!box || ready) return undefined;
    const frame = requestAnimationFrame(() => setReady(true));
    return () => cancelAnimationFrame(frame);
  }, [box, ready]);

  return { group, box, ready };
}
