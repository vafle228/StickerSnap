import { useState, type ReactNode } from "react";
import { Button, Icon, usePresence } from "../../../ds";
import { cx } from "../../../ds/cx";
import type { Segment } from "../../../shared/api/types";
import { t } from "../../../shared/strings/ru";
import { glideNext } from "../glide";
import { num } from "../format";
import { segmentDuration } from "../model/frame";
import { useEditor } from "../model/store";
import styles from "./TimelineSlot.module.css";
import { addSegment, deleteSegment, type TimelineInfo } from "./useTimeline";

interface Shown {
  segment: Segment;
  leaving: boolean;
}

/** The segments as drawn: the scene's, plus the deleted ones still fading out at their place. */
function withLeaving(previous: Shown[], segments: readonly Segment[]): Shown[] {
  const next: Shown[] = segments.map((segment) => ({ segment, leaving: false }));
  previous.forEach((p, i) => {
    if (segments.some((s) => s.id === p.segment.id)) return;
    next.splice(Math.min(i, next.length), 0, { segment: p.segment, leaving: true });
  });
  return next;
}

const same = (a: Shown[], b: Shown[]) =>
  a.length === b.length &&
  a.every((x, i) => x.segment === b[i]?.segment && x.leaving === b[i]?.leaving);

/** One tab in its slot: opens its room when added, fades and closes it when deleted. */
function TabSlot({
  leaving,
  onExited,
  children,
}: {
  leaving: boolean;
  onExited: () => void;
  children: ReactNode;
}) {
  const { mounted, state, appear, ref } = usePresence(!leaving, onExited);
  if (!mounted) return null;
  return (
    <span
      ref={ref}
      className={styles.tabSlot}
      data-state={state}
      data-appear={appear ? undefined : "false"}
    >
      {children}
    </span>
  );
}

/** «Стикер 1 · 2,84 с ×», one per segment, and «+ Отрезок» (R18, artboard 3a). The cross deletes
 *  the segment at once; the popup brings it back. */
export function SegmentTabs({ info }: { info: TimelineInfo }) {
  const { segments, segment, fps } = info;
  const [shown, setShown] = useState<Shown[]>(() => withLeaving([], segments));
  const next = withLeaving(shown, segments);
  if (!same(next, shown)) setShown(next);

  const choose = (id: string) => {
    const { select, setTime, setPlaying } = useEditor.getState();
    const target = segments.find((s) => s.id === id);
    if (!target || id === segment?.id) return;
    setPlaying(false);
    // The window on the strip and the frame on the canvas glide to the chosen segment.
    glideNext();
    select(id);
    setTime(target.inFrame / fps);
  };

  return (
    <div className={styles.tabs} role="group" aria-label={t("editor.segments")}>
      {next.map(({ segment: s, leaving }) => (
        <TabSlot
          key={s.id}
          leaving={leaving}
          onExited={() => setShown((list) => list.filter((x) => x.segment.id !== s.id))}
        >
          <span className={cx(styles.tab, s.id === segment?.id && styles.chosen)}>
            <button
              type="button"
              className={styles.tabName}
              aria-pressed={s.id === segment?.id}
              onClick={() => choose(s.id)}
            >
              {t("editor.segment.tab", { name: s.name, duration: num(segmentDuration(s, fps), 2) })}
            </button>
            <button
              type="button"
              className={styles.tabDelete}
              aria-label={t("editor.segment.delete", { name: s.name })}
              title={t("editor.segment.delete", { name: s.name })}
              onClick={() => deleteSegment(info, s.id)}
            >
              <Icon name="close" size={14} />
            </button>
          </span>
        </TabSlot>
      ))}
      <Button
        size="sm"
        variant="ghost"
        icon="plus"
        title={t("editor.segment.addHint")}
        onClick={() => addSegment(info)}
      >
        {t("editor.segment.add")}
      </Button>
    </div>
  );
}
