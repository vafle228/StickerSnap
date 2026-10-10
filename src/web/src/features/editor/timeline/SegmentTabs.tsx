import { Button, Icon } from "../../../ds";
import { cx } from "../../../ds/cx";
import { t } from "../../../shared/strings/ru";
import { num } from "../format";
import { segmentDuration } from "../model/frame";
import { useEditor } from "../model/store";
import styles from "./TimelineSlot.module.css";
import { addSegment, deleteSegment, type TimelineInfo } from "./useTimeline";

/** «Стикер 1 · 2,84 с ×», one per segment, and «+ Отрезок» (R18, artboard 3a). The cross deletes
 *  the segment at once; the popup brings it back. */
export function SegmentTabs({ info }: { info: TimelineInfo }) {
  const { segments, segment, fps } = info;

  const choose = (id: string) => {
    const { select, setTime, setPlaying } = useEditor.getState();
    const next = segments.find((s) => s.id === id);
    if (!next || id === segment?.id) return;
    setPlaying(false);
    select(id);
    setTime(next.inFrame / fps);
  };

  return (
    <div className={styles.tabs} role="group" aria-label={t("editor.segments")}>
      {segments.map((s) => (
        <span key={s.id} className={cx(styles.tab, s.id === segment?.id && styles.chosen)}>
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
