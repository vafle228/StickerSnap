import { useRef, type CSSProperties, type KeyboardEvent, type PointerEvent } from "react";
import { cx } from "../../../ds/cx";
import type { PresetLimits, Segment } from "../../../shared/api/types";
import { t } from "../../../shared/strings/ru";
import {
  cropPreset,
  moveCrop,
  presetAspect,
  resizeCrop,
  resizeStretch,
  stretchBox,
  stretchOf,
  type Corner,
  type Side,
} from "../model/crop";
import { frameInput, outputSize, type Size } from "../model/frame";
import { editSegment, useEditor } from "../model/store";
import styles from "./VideoStage.module.css";

type Frame = Segment["frame"];

const CORNERS: readonly Corner[] = ["nw", "ne", "sw", "se"];
const SIDES: readonly Side[] = ["n", "e", "s", "w"];

/** The shortest side a frame can be dragged down to: 16 source pixels, and never so small on
 *  the screen that its handles run into each other. */
const MIN_SOURCE = 16;
const MIN_SCREEN = 32;

/** What is being dragged, and the frame as it was when the drag began. */
type Gesture =
  | { kind: "move"; start: Frame }
  | { kind: "corner"; corner: Corner; start: Frame }
  | { kind: "side"; side: Side; start: Frame };

interface CropFrameProps {
  /** What is drawn: the committed frame or the one being dragged */
  frame: Frame;
  preset: Segment["preset"];
  source: Size;
  limits?: Pick<PresetLimits, "side">;
  /** Display px per source px */
  scale: number;
  /** «Кроп» is on: the frame has handles, can be dragged and shows its sizes */
  editable: boolean;
}

/** What goes into the sticker, drawn over the source (artboard 3a): the crop with the rest
 *  dimmed, or the box a stretch squeezes the source into. With «Кроп» on, the crop moves by its
 *  body and resizes by its corners holding the preset's proportion (Shift frees it); a stretch
 *  changes its width or height by the side handles. A drag is drawn from frameDraft and lands
 *  in the scene as one edit on release; the playhead stays where it is. */
export function CropFrame({ frame, preset, source, limits, scale, editable }: CropFrameProps) {
  const gesture = useRef<{ what: Gesture; x: number; y: number } | null>(null);
  const min = Math.min(source.width, source.height, Math.max(MIN_SOURCE, MIN_SCREEN / scale));

  const crop = frame.mode === "crop";
  const box = crop ? frame.rect : stretchBox(frame.aspect, source);
  const style: CSSProperties = {
    left: box.x * scale,
    top: box.y * scale,
    width: box.width * scale,
    height: box.height * scale,
  };
  const input = frameInput(frame, source);
  const output = limits && outputSize(frame, preset, limits);

  /** The frame a gesture makes when the pointer is `dx`, `dy` source pixels from its start. */
  const dragged = (what: Gesture, dx: number, dy: number, free: boolean): Frame => {
    const { start } = what;
    if (start.mode === "stretch") {
      if (what.kind !== "side") return start;
      const { side } = what;
      const delta = side === "e" ? dx : side === "w" ? -dx : side === "s" ? dy : -dy;
      const from = stretchBox(start.aspect, source);
      return stretchOf(resizeStretch(from, side, delta, source, min), source);
    }
    if (what.kind === "move") return { ...start, rect: moveCrop(start.rect, dx, dy, source) };
    if (what.kind !== "corner") return start;
    const aspect = free ? undefined : presetAspect(start.preset, source);
    const rect = resizeCrop(start.rect, what.corner, dx, dy, source, aspect, min);
    const next = aspect === undefined ? cropPreset(rect, source) : start.preset;
    return { mode: "crop", preset: next, rect };
  };

  const commit = (next: Frame, before: Frame) => {
    if (JSON.stringify(next) !== JSON.stringify(before)) editSegment((s) => void (s.frame = next));
  };

  const onPointerDown = (e: PointerEvent<HTMLDivElement>) => {
    if (!editable || e.button !== 0) return;
    const target = e.target as HTMLElement;
    const corner = target.closest<HTMLElement>("[data-corner]")?.dataset.corner as
      Corner | undefined;
    const side = target.closest<HTMLElement>("[data-side]")?.dataset.side as Side | undefined;
    let what: Gesture;
    if (corner) what = { kind: "corner", corner, start: frame };
    else if (side) what = { kind: "side", side, start: frame };
    else if (crop) what = { kind: "move", start: frame };
    else return;
    gesture.current = { what, x: e.clientX, y: e.clientY };
    e.currentTarget.setPointerCapture(e.pointerId);
  };

  const onPointerMove = (e: PointerEvent<HTMLDivElement>) => {
    const current = gesture.current;
    if (!current) return;
    const dx = (e.clientX - current.x) / scale;
    const dy = (e.clientY - current.y) / scale;
    useEditor.getState().setFrameDraft(dragged(current.what, dx, dy, e.shiftKey));
  };

  const onPointerUp = (e: PointerEvent<HTMLDivElement>) => {
    const current = gesture.current;
    gesture.current = null;
    const { frameDraft, setFrameDraft } = useEditor.getState();
    setFrameDraft(undefined);
    if (current && frameDraft && e.type !== "pointercancel") commit(frameDraft, current.what.start);
  };

  // Arrows on the focused frame or handle nudge it by a source pixel (Shift: ten); everywhere
  // else they stay the playhead's.
  const onKeyDown = (what: Gesture) => (e: KeyboardEvent<HTMLElement>) => {
    const step = e.shiftKey ? 10 : 1;
    const delta = ARROWS[e.key];
    if (!editable || !delta) return;
    e.preventDefault();
    e.stopPropagation();
    commit(dragged(what, delta[0] * step, delta[1] * step, false), frame);
  };

  return (
    <>
      {crop && (
        <div className={styles.shade} aria-hidden>
          <div className={styles.hole} style={style} />
        </div>
      )}
      <div
        className={cx(crop ? styles.crop : styles.stretch, editable && styles.editable)}
        style={style}
        role={editable && crop ? "group" : undefined}
        tabIndex={editable && crop ? 0 : undefined}
        aria-label={editable ? t(crop ? "editor.cropFrame" : "editor.stretchFrame") : undefined}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        onPointerCancel={onPointerUp}
        onKeyDown={crop ? onKeyDown({ kind: "move", start: frame }) : undefined}
      >
        {editable && crop && <span className={styles.thirds} aria-hidden />}
        {editable &&
          crop &&
          CORNERS.map((corner) => (
            <span
              key={corner}
              data-corner={corner}
              role="button"
              tabIndex={0}
              aria-label={t(`editor.cropCorner.${corner}`)}
              className={cx(styles.corner, styles[corner])}
              onKeyDown={onKeyDown({ kind: "corner", corner, start: frame })}
            />
          ))}
        {editable &&
          !crop &&
          SIDES.map((side) => (
            <span
              key={side}
              data-side={side}
              role="button"
              tabIndex={0}
              aria-label={t(`editor.stretchSide.${side}`)}
              className={cx(styles.side, styles[side])}
              onKeyDown={onKeyDown({ kind: "side", side, start: frame })}
            />
          ))}
        {editable && output && (
          <span className={styles.frameLabel}>
            {t(crop ? "editor.frame.crop" : "editor.frame.stretch", {
              inW: input.width,
              inH: input.height,
              outW: output.width,
              outH: output.height,
            })}
          </span>
        )}
      </div>
    </>
  );
}

const ARROWS: Partial<Record<string, readonly [number, number]>> = {
  ArrowLeft: [-1, 0],
  ArrowRight: [1, 0],
  ArrowUp: [0, -1],
  ArrowDown: [0, 1],
};
