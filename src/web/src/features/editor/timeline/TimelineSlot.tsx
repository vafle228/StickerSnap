import type { CSSProperties } from "react";
import { Card, Icon, IconButton } from "../../../ds";
import { useHotkey } from "../../../shared/hotkeys";
import { t } from "../../../shared/strings/ru";
import { num, timecode } from "../format";
import { frameCount, segmentDuration } from "../model/frame";
import { useEditor } from "../model/store";
import { frameAt, timeOf } from "../model/timeline";
import { FrameStrip } from "./FrameStrip";
import { SegmentTabs } from "./SegmentTabs";
import styles from "./TimelineSlot.module.css";
import { markEdge, stepFrame, useTimeline, type TimelineInfo } from "./useTimeline";

/** The sunken zone under the canvas (artboards 3a, 3b): the segments, the transport, the frame
 *  strip, the I/O read-outs and the «Обзор ↔ Кадры» scale. `active` is false while another
 *  section covers the editor: its keys stay silent then. */
export function TimelineSlot({ active }: { active: boolean }) {
  const info = useTimeline();
  const playing = useEditor((s) => s.playing);
  const setPlaying = useEditor((s) => s.setPlaying);

  const on = active && info !== undefined;
  useHotkey("prevFrame", () => info && stepFrame(info, -1), on);
  useHotkey("nextFrame", () => info && stepFrame(info, 1), on);
  useHotkey("markIn", () => info && markEdge(info, "in"), on);
  useHotkey("markOut", () => info && markEdge(info, "out"), on);

  if (!info) return null;
  const { segment, fps, maxDuration, bounds } = info;
  const label = t(playing ? "editor.pause" : "editor.play");
  const over = frameCount(segment) > bounds.max;

  return (
    <Card variant="sunken" className={styles.timeline} aria-label={t("editor.timeline")}>
      <div className={styles.head}>
        <SegmentTabs info={info} />
        <Timecode fps={fps} />
      </div>

      <div className={styles.main}>
        <div className={styles.transport}>
          <IconButton
            icon="chevron-left"
            label={t("editor.prevFrame")}
            flat
            size="sm"
            className={styles.step}
            onClick={() => stepFrame(info, -1)}
          />
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
          <IconButton
            icon="chevron-right"
            label={t("editor.nextFrame")}
            flat
            size="sm"
            className={styles.step}
            onClick={() => stepFrame(info, 1)}
          />
        </div>
        <FrameStrip info={info} />
      </div>

      <div className={styles.foot}>
        <span className={styles.edge}>
          {t("editor.in", { time: timecode(timeOf(segment.inFrame, fps)) })}
        </span>
        <span className={styles.edge}>
          {t("editor.out", { time: timecode(timeOf(segment.outFrame + 1, fps)) })}
        </span>
        {maxDuration !== undefined && (
          <span className={over ? styles.over : styles.length}>
            {t("editor.length", {
              duration: num(segmentDuration(segment, fps), 2),
              max: num(maxDuration, 2),
            })}
          </span>
        )}
        <span className={styles.spacer} />
        <Scale info={info} />
      </div>
    </Card>
  );
}

/** «00:02.640 · кадр 79»: its own subscription to the clock. */
function Timecode({ fps }: { fps: number }) {
  const time = useEditor((s) => s.time);
  return (
    <span className={styles.time}>
      {t("editor.timecode", { time: timecode(time), frame: frameAt(time, fps) })}
    </span>
  );
}

/** «Обзор ↔ Кадры»: the whole video in the strip's width, or a cell with a number per frame. */
function Scale({ info }: { info: TimelineInfo }) {
  const zoom = useEditor((s) => s.timelineZoom);
  const setZoom = useEditor((s) => s.setTimelineZoom);
  return (
    <label className={styles.scale}>
      {t("editor.scale.overview")}
      <span className={`ss-slider ${styles.scaleSlider}`}>
        <input
          type="range"
          min={0}
          max={1}
          step={0.01}
          value={zoom}
          aria-label={t("editor.scale")}
          aria-valuetext={`${Math.round(zoom * 100)}%`}
          disabled={info.total < 2}
          style={{ "--ss-fill": `${zoom * 100}%` } as CSSProperties}
          onChange={(e) => setZoom(Number(e.target.value))}
        />
      </span>
      {t("editor.scale.frames")}
    </label>
  );
}
