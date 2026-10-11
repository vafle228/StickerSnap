import { useEffect, useState } from "react";
import { prefersReducedMotion } from "../../ds/hooks";

/* Jumps made by a click glide: a frame preset, «Скопировать кадр во все стикеры», another
   segment tab, «Вписать целиком», the zoom buttons. Drags, keys and undo land at once
   (docs/ui-motion.md, principle 4). The store does not know where a change came from, so the
   button says so right before it edits, and the moved element glides if it changes soon after. */

/** A change this soon after glideNext() came from that click */
const WINDOW_MS = 100;
/** duration-base with room: the class stays while the transition runs */
const GLIDE_MS = 250;

let requested = Number.NEGATIVE_INFINITY;

/** Called by a button just before its edit: the next jump glides. */
export function glideNext() {
  requested = performance.now();
}

/** Whether the element drawn from `key` should glide to where it is now: true for a moment
 *  after `key` changes right after glideNext(), never while `held` (a drag is on). */
export function useGlide(key: string, held = false): boolean {
  const [last, setLast] = useState(key);
  const [gliding, setGliding] = useState(false);
  if (key !== last) {
    setLast(key);
    setGliding(!held && performance.now() - requested < WINDOW_MS && !prefersReducedMotion());
  }
  if (held && gliding) setGliding(false);

  useEffect(() => {
    if (!gliding) return undefined;
    const timer = window.setTimeout(() => setGliding(false), GLIDE_MS);
    return () => window.clearTimeout(timer);
  }, [gliding, last]);

  return gliding;
}
