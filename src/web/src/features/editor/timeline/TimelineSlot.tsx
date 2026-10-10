import { Card, Icon } from "../../../ds";
import { useAppInfo } from "../../../shared/api/queries";
import { t } from "../../../shared/strings/ru";
import { num, timecode } from "../format";
import { fpsOf, segmentDuration } from "../model/frame";
import { useEditor, useSegment } from "../model/store";
import styles from "./TimelineSlot.module.css";

/** The sunken zone under the canvas. The FrameStrip with I/O handles, frame steps, the 3,00 s
 *  stop and the segment tabs are the timeline task; this holds play/pause and the read-outs. */
export function TimelineSlot() {
  const segment = useSegment();
  const source = useEditor((s) => s.source);
  const playing = useEditor((s) => s.playing);
  const time = useEditor((s) => s.time);
  const setPlaying = useEditor((s) => s.setPlaying);
  const limits = useAppInfo().data?.limits;
  if (!segment || !source) return null;

  const fps = fpsOf(source);
  const max = limits?.[segment.preset].maxDurationSec;
  const label = t(playing ? "editor.pause" : "editor.play");

  return (
    <Card variant="sunken" className={styles.timeline} aria-label={t("editor.timeline")}>
      {/* The DS has no primary IconButton: its shape with the primary fill of artboard 3a. */}
      <button
        type="button"
        className={`ss-iconbtn ${styles.play}`}
        aria-label={label}
        title={label}
        onClick={() => setPlaying(!playing)}
      >
        <Icon name={playing ? "pause" : "play"} size={18} />
      </button>
      <span className={styles.time}>
        {t("editor.timecode", { time: timecode(time), frame: Math.floor(time * fps + 1e-6) })}
      </span>
      {max !== undefined && (
        <span className={styles.length}>
          {t("editor.length", {
            duration: num(segmentDuration(segment, fps), 2),
            max: num(max, 2),
          })}
        </span>
      )}
    </Card>
  );
}
