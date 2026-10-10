import { useEffect, useRef, type CSSProperties } from "react";
import { Button, Card, Icon, IconButton, TONE_ICON } from "../../../ds";
import { useHotkey } from "../../../shared/hotkeys";
import { plural, t } from "../../../shared/strings/ru";
import { num, speed as speedText, timecode } from "../format";
import { frameCount, segmentDuration } from "../model/frame";
import { useEditor } from "../model/store";
import { frameAt, timeOf } from "../model/timeline";
import { FrameStrip } from "./FrameStrip";
import { SegmentTabs } from "./SegmentTabs";
import styles from "./TimelineSlot.module.css";
import {
  addSegment,
  fitStop,
  markEdge,
  overLimit,
  setEdges,
  speedUp,
  stepFrame,
  stopShown,
  useTimeline,
  type TimelineInfo,
} from "./useTimeline";

/** The sunken zone under the canvas (artboards 3a, 3b): the segments with the «Обзор ↔ Кадры»
 *  scale, the transport, the frame strip, the I/O read-outs and the stop hint under it. The
 *  scale sits up by the tabs, not in the bottom row as on 3a: so the hint fits in that row.
 *  `active` is false while another section covers the editor: its keys stay silent then. */
export function TimelineSlot({ active }: { active: boolean }) {
  const info = useTimeline();
  const playing = useEditor((s) => s.playing);
  const setPlaying = useEditor((s) => s.setPlaying);
  const stop = useEditor((s) => s.stop);

  const on = active && info !== undefined;
  useHotkey("prevFrame", () => info && stepFrame(info, -1), on);
  useHotkey("nextFrame", () => info && stepFrame(info, 1), on);
  useHotkey("markIn", () => info && markEdge(info, "in"), on);
  useHotkey("markOut", () => info && markEdge(info, "out"), on);

  if (!info) return null;
  const { segment, fps, maxDuration, bounds } = info;
  const label = t(playing ? "editor.pause" : "editor.play");
  const over = segment !== undefined && frameCount(segment) > bounds.max;
  // The notes say the length and more, so they take its place.
  const noted = over || stopShown(info, stop) !== undefined;

  return (
    <Card variant="sunken" className={styles.timeline} aria-label={t("editor.timeline")}>
      <div className={styles.head}>
        <SegmentTabs info={info} />
        <Scale info={info} />
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
        {segment ? (
          <>
            <span className={styles.edge}>
              {t("editor.in", { time: timecode(timeOf(segment.inFrame, fps)) })}
            </span>
            <span className={styles.edge}>
              {t("editor.out", { time: timecode(timeOf(segment.outFrame + 1, fps)) })}
            </span>
            {maxDuration !== undefined && !noted && (
              <span className={styles.length}>
                {t("editor.length", {
                  duration: num(segmentDuration(segment, fps), 2),
                  max: num(maxDuration, 2),
                })}
              </span>
            )}
          </>
        ) : (
          <span className={styles.none}>{t("editor.segment.none")}</span>
        )}
        <Note info={info} />
      </div>
    </Card>
  );
}

/** Under the strip, in the timeline's own row, from the state and not from what caused it:
 *  - the segment plays longer than the limit (a slower speed did it, or anything else): it
 *    stays until fixed, with «Ускорить до …» and «Укоротить до …», as on artboard 3c G;
 *  - an edge was pushed against the stop (artboard 3b, B): «Вписать целиком», a click
 *    elsewhere puts it away;
 *  - frames are picked: «Сделать отрезком». */
function Note({ info }: { info: TimelineInfo }) {
  const stop = stopShown(
    info,
    useEditor((s) => s.stop),
  );
  const selection = useEditor((s) => s.selection);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!stop) return undefined;
    const away = (e: PointerEvent) => {
      if (!ref.current?.contains(e.target as Node)) useEditor.getState().setStop(undefined);
    };
    document.addEventListener("pointerdown", away);
    return () => document.removeEventListener("pointerdown", away);
  }, [stop]);

  const over = overLimit(info);
  if (over && info.segment && info.maxDuration !== undefined) {
    const { segment, fps, maxDuration } = info;
    return (
      <div className={`${styles.note} ${styles.warn}`} role="alert">
        <Icon name={TONE_ICON.warning} size={16} strokeWidth={2} />
        {t("editor.over", {
          duration: num(segmentDuration(segment, fps), 2),
          speed: speedText(segment.speed),
          max: num(maxDuration, 2),
        })}
        {over.speed !== undefined && (
          <Button
            size="sm"
            variant="ghost"
            className={styles.noteAction}
            onClick={() => over.speed !== undefined && speedUp(over.speed)}
          >
            {t("editor.over.speedUp", { speed: speedText(over.speed) })}
          </Button>
        )}
        <Button
          size="sm"
          variant="ghost"
          className={styles.noteAction}
          onClick={() => setEdges(segment, over.range)}
        >
          {t("editor.over.shorten", {
            duration: num(segmentDuration({ ...segment, ...over.range }, fps), 2),
          })}
        </Button>
      </div>
    );
  }
  if (stop && info.segment && info.maxDuration !== undefined) {
    return (
      <div ref={ref} className={`${styles.note} ${styles.warn}`} role="alert">
        <Icon name={TONE_ICON.warning} size={16} strokeWidth={2} />
        {t("editor.limit", {
          max: num(info.maxDuration, 2),
          speed: speedText(info.segment.speed),
        })}
        <Button
          size="sm"
          variant="ghost"
          className={styles.noteAction}
          onClick={() => fitStop(info)}
        >
          {t("editor.limit.fit")}
        </Button>
      </div>
    );
  }
  if (selection && selection.to > selection.from) {
    const n = selection.to - selection.from + 1;
    return (
      <div className={styles.note} role="status">
        {t(`editor.selection.${plural(n)}`, { n })}
        <Button
          size="sm"
          variant="ghost"
          icon="plus"
          className={styles.noteAction}
          onClick={() => addSegment(info)}
        >
          {t("editor.selection.make")}
        </Button>
      </div>
    );
  }
  return null;
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
