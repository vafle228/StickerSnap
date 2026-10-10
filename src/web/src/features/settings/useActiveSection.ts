import { useEffect, useState, type RefObject } from "react";

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

/** Scroll spy of the table of contents. A click sets the section at once; the smooth scroll
 *  that follows lands on the same one. */
export function useActiveSection<Id extends string>(
  scroller: RefObject<HTMLElement | null>,
  ids: readonly Id[],
): [Id | undefined, (id: Id) => void] {
  const [active, setActive] = useState<Id | undefined>(ids[0]);

  useEffect(() => {
    const box = scroller.current;
    if (!box) return undefined;
    const onScroll = () => {
      const top = box.getBoundingClientRect().top;
      const tops = ids.map(
        (id) => (document.getElementById(id)?.getBoundingClientRect().top ?? 0) - top,
      );
      const atEnd = box.scrollTop + box.clientHeight >= box.scrollHeight - 2;
      setActive(ids[sectionAt(tops, atEnd)]);
    };
    box.addEventListener("scroll", onScroll, { passive: true });
    return () => box.removeEventListener("scroll", onScroll);
  }, [scroller, ids]);

  return [active, setActive];
}
