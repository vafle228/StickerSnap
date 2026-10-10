import { Button } from "../../../ds";
import { t } from "../../../shared/strings/ru";
import { num } from "../format";
import { segmentDuration } from "../model/frame";
import { useEditor } from "../model/store";
import styles from "./TimelineSlot.module.css";
import { addSegment, type TimelineInfo } from "./useTimeline";

/** «Стикер 1 · 2,84 с», one per segment, and «+ Отрезок» (R18, artboard 3a). */
export function SegmentTabs({ info }: { info: TimelineInfo }) {
  const { segments, segment, fps } = info;

  const choose = (id: string) => {
    const { select, setTime, setPlaying } = useEditor.getState();
    const next = segments.find((s) => s.id === id);
    if (!next || id === segment.id) return;
    setPlaying(false);
    select(id);
    setTime(next.inFrame / fps);
  };

  return (
    <div className={styles.tabs} role="group" aria-label={t("editor.segments")}>
      {segments.map((s) => (
        <Button
          key={s.id}
          size="sm"
          aria-pressed={s.id === segment.id}
          className={styles.tab}
          onClick={() => choose(s.id)}
        >
          {t("editor.segment.tab", { name: s.name, duration: num(segmentDuration(s, fps), 2) })}
        </Button>
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
