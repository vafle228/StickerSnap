import type { Segment } from "../../../shared/api/types";
import { fitSpeed, SPEED_MAX } from "./frame";

/* The timeline's math (artboard 3b): frames of the source, the I and O edges of a segment,
   the 3,00 s stop and the «Обзор ↔ Кадры» scale. Frames are what the scene keeps (contract,
   «Время в сцене»); seconds are only shown. */

/** Allows for float noise: 2.64 s at 30 fps is frame 79, not 78.99999. */
const EPS = 1e-6;

/** The frame the time falls into. */
export function frameAt(time: number, fps: number): number {
  return Math.floor(time * fps + EPS);
}

/** The time a frame starts at. */
export function timeOf(frame: number, fps: number): number {
  return frame / fps;
}

export function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value));
}

/** The playhead one frame back or forward, held inside the source. */
export function stepTime(time: number, delta: number, fps: number, total: number): number {
  return timeOf(clamp(frameAt(time, fps) + delta, 0, total - 1), fps);
}

/** How many frames the limit lets a segment play at its speed. */
export function maxFrames(maxDurationSec: number, fps: number, speed: number): number {
  return Math.max(1, Math.floor(maxDurationSec * fps * speed + EPS));
}

type Range = Pick<Segment, "inFrame" | "outFrame">;
type Cut = Range & Pick<Segment, "excludedFrames">;

/** Excluded frames (P1) between two edges, both included. */
function excludedIn(from: number, to: number, excluded: readonly number[]): number {
  const [a, b] = from <= to ? [from, to] : [to, from];
  return excluded.filter((f) => f >= a && f <= b).length;
}

/** The farthest frame from `start` in `direction` that keeps at most `max` frames playing. */
function reach(start: number, direction: 1 | -1, max: number, excluded: readonly number[]) {
  let end = start + direction * (max - 1);
  // Excluded frames inside the range do not play, so they give room: widen until it settles.
  for (;;) {
    const next = start + direction * (max - 1 + excludedIn(start, end, excluded));
    if (next === end) return end;
    end = next;
  }
}

export type Edge = "in" | "out";

export interface Bounds {
  /** Frames in the source */
  total: number;
  /** Frames the limit allows at the segment's speed; Infinity while the limits are unknown */
  max: number;
}

export interface Moved extends Range {
  /** The moved edge stopped at the limit before reaching the wanted frame (artboard 3b, B) */
  limited: boolean;
}

/** Moves one edge of the segment to `frame` (for O: the last frame that plays).
 *  A dragged edge stops at the other one; I and O pressed past the other edge (`push`) take it
 *  along, leaving a one-frame segment. The moved edge stops where the segment reaches the limit;
 *  a segment that is already longer (its speed was lowered) can shrink but not grow. */
export function moveEdge(
  segment: Cut,
  edge: Edge,
  frame: number,
  { total, max }: Bounds,
  push = false,
): Moved {
  const wanted = clamp(frame, 0, total - 1);
  const excluded = segment.excludedFrames;
  if (edge === "in") {
    const outFrame = push ? Math.max(segment.outFrame, wanted) : segment.outFrame;
    const stop = Number.isFinite(max) ? reach(outFrame, -1, max, excluded) : 0;
    const floor = Math.min(Math.max(0, stop), segment.inFrame);
    const inFrame = clamp(wanted, floor, outFrame);
    return { inFrame, outFrame, limited: wanted < floor };
  }
  const inFrame = push ? Math.min(segment.inFrame, wanted) : segment.inFrame;
  const stop = Number.isFinite(max) ? reach(inFrame, 1, max, excluded) : total - 1;
  const ceiling = Math.max(Math.min(total - 1, stop), segment.outFrame);
  const outFrame = clamp(wanted, inFrame, ceiling);
  return { inFrame, outFrame, limited: wanted > ceiling };
}

/** «Вписать целиком» from the stop hint: the edge goes where it was pushed to and the speed rises
 *  so the whole segment fits the limit. Past what even 4× can fit, the edge stops at that. */
export function fitWhole(
  segment: Cut,
  edge: Edge,
  wanted: number,
  fps: number,
  maxDurationSec: number,
  total: number,
): Range & Pick<Segment, "speed"> {
  const max = maxFrames(maxDurationSec, fps, SPEED_MAX);
  const { inFrame, outFrame } = moveEdge(segment, edge, wanted, { total, max });
  const range = { inFrame, outFrame, excludedFrames: segment.excludedFrames };
  return { inFrame, outFrame, speed: fitSpeed(range, fps, maxDurationSec) };
}

/** Frames between two clicks, in order: Shift+click in the «Кадры» scale. */
export interface Selection {
  from: number;
  to: number;
}

export function orderedSelection(anchor: number, frame: number): Selection {
  return { from: Math.min(anchor, frame), to: Math.max(anchor, frame) };
}

/** Where «+ Отрезок» puts a new segment: the selection if there is one, else from the playhead
 *  for as long as the limit allows at 1×. */
export function newRange(
  playhead: number,
  selection: Selection | undefined,
  { total, max }: Bounds,
): Range {
  if (selection && selection.to > selection.from) {
    // A selection longer than the limit gives a segment up to the stop.
    const end = Number.isFinite(max) ? selection.from + max - 1 : selection.to;
    return { inFrame: selection.from, outFrame: Math.min(selection.to, end) };
  }
  const inFrame = clamp(playhead, 0, total - 1);
  const length = Number.isFinite(max) ? max : total;
  return { inFrame, outFrame: Math.min(total - 1, inFrame + length - 1) };
}

/** The next free «Стикер N»: one more than the biggest number taken. */
export function nextNumber(names: readonly string[]): number {
  const taken = names.map((name) => Number(/(\d+)\s*$/.exec(name)?.[1] ?? 0));
  return Math.max(0, ...taken) + 1;
}

/** A new segment takes the frame, the preset and the loop of the one it was made next to. */
export function newSegment(base: Segment, range: Range, id: string, name: string): Segment {
  return {
    id,
    name,
    inFrame: range.inFrame,
    outFrame: range.outFrame,
    excludedFrames: [],
    frame: structuredClone(base.frame),
    speed: 1,
    loop: base.loop,
    preset: base.preset,
    longSticker: false,
  };
}

/* The scale: 0 is «Обзор», the whole video in the strip's width; 1 is «Кадры» at its widest.
   The slider runs on a log scale, so each step zooms by the same factor. */

/** «Кадры» begins here: every frame gets its own cell with a number under it (artboard 3b, C). */
export const FRAME_PX = 24;
/** The right end of the scale */
export const FRAME_PX_MAX = 48;

export function pxPerFrame(zoom: number, width: number, total: number): number {
  const fit = width / Math.max(1, total);
  const top = Math.max(fit, FRAME_PX_MAX);
  return fit * (top / fit) ** clamp(zoom, 0, 1);
}

/** The zoom that gives `px` per frame: the scale snaps to «Кадры» with it. */
export function zoomFor(px: number, width: number, total: number): number {
  const fit = width / Math.max(1, total);
  const top = Math.max(fit, FRAME_PX_MAX);
  if (top <= fit) return 0;
  return clamp(Math.log(px / fit) / Math.log(top / fit), 0, 1);
}

const TICKS = [0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600];

/** Seconds between ruler labels: the smallest step that leaves `minPx` between them. */
export function tickStep(pxPerSecond: number, minPx = 56): number {
  return TICKS.find((step) => step * pxPerSecond >= minPx) ?? 600;
}
