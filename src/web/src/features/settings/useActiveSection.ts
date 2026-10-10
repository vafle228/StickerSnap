import { useCallback, useEffect, useRef, useState, type RefObject } from "react";

/** The section whose top has passed this far below the top of the scroll */
const OFFSET = 96;

/** Which section the scroll shows: the last one whose top is above the offset line, or the
 *  last section when the scroll is at its end (a short last section never reaches the line). */
export function sectionAt(tops: readonly number[], atEnd: boolean): number {
  if (atEnd) return tops.length - 1;
  let index = 0;
  tops.forEach((top, i) => {
    if (top <= OFFSET) index = i;
  });
  return index;
}

/** How long a click on the contents holds its section if the browser never sends scrollend */
const HOLD_MS = 1000;

/** Scroll spy of the table of contents. A click picks its section and holds it while the
 *  smooth scroll runs: a section near the end («Горячие клавиши») may never reach the line,
 *  and the end of the scroll must not hand the highlight over to the last one. The hold ends
 *  with the scroll, or as soon as the user scrolls by hand. */
export function useActiveSection<Id extends string>(
  scroller: RefObject<HTMLElement | null>,
  ids: readonly Id[],
): [Id | undefined, (id: Id) => void] {
  const [active, setActive] = useState<Id | undefined>(ids[0]);
  const held = useRef(false);
  const timer = useRef<ReturnType<typeof setTimeout>>();

  useEffect(() => {
    const box = scroller.current;
    if (!box) return undefined;
    const release = () => {
      held.current = false;
      clearTimeout(timer.current);
    };
    const onScroll = () => {
      if (held.current) return;
      const top = box.getBoundingClientRect().top;
      const tops = ids.map(
        (id) => (document.getElementById(id)?.getBoundingClientRect().top ?? 0) - top,
      );
      const atEnd = box.scrollTop + box.clientHeight >= box.scrollHeight - 2;
      setActive(ids[sectionAt(tops, atEnd)]);
    };
    const byHand = ["wheel", "touchstart", "keydown", "pointerdown"] as const;
    box.addEventListener("scroll", onScroll, { passive: true });
    box.addEventListener("scrollend", release);
    for (const type of byHand) box.addEventListener(type, release, { passive: true });
    return () => {
      release();
      box.removeEventListener("scroll", onScroll);
      box.removeEventListener("scrollend", release);
      for (const type of byHand) box.removeEventListener(type, release);
    };
  }, [scroller, ids]);

  const select = useCallback((id: Id) => {
    setActive(id);
    held.current = true;
    clearTimeout(timer.current);
    if (!("onscrollend" in window))
      timer.current = setTimeout(() => (held.current = false), HOLD_MS);
  }, []);

  return [active, select];
}
