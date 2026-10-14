import { useAppInfo } from "../../../shared/api/queries";
import { notify } from "../../../shared/notify";
import type { Segment, SourceInfo } from "../../../shared/api/types";
import { t } from "../../../shared/strings/ru";
import {
  fitWhole,
  frameAt,
  shorten,
  maxFrames,
  moveEdge,
  newRange,
  newSegment,
  nextNumber,
  stepTime,
} from "../model/timeline";
import type { Bounds, Edge } from "../model/timeline";
import { fitSpeed, fpsOf, frameCount, playLimit, SPEED_MAX } from "../model/frame";
import { editSegment, useEditor, useSegment, type Stop } from "../model/store";

/** What every part of the timeline works with: the segments, the selected one and its frame
 *  bounds. A project may have no segments: then there is no selected one and nothing to trim. */
export interface TimelineInfo {
  projectId: string;
  source: SourceInfo;
  segments: Segment[];
  segment?: Segment;
  fps: number;
  /** Frames in the source */
  total: number;
  /** How long the selected segment may play, seconds: its preset's limit, or a long sticker's
   *  (R19); unknown until GET /api/v1/app answers */
  maxDuration?: number;
  /** The limit of a new segment, which is never a long sticker */
  newDuration?: number;
  bounds: Bounds;
}

export function useTimeline(): TimelineInfo | undefined {
  const projectId = useEditor((s) => s.projectId);
  const source = useEditor((s) => s.source);
  const segments = useEditor((s) => s.scene?.segments);
  const segment = useSegment();
  const limits = useAppInfo().data?.limits;
  if (!projectId || !source || !segments) return undefined;

  const fps = fpsOf(source);
  const total = Math.max(1, source.frameCount);
  const preset = limits?.[segment?.preset ?? "sticker"];
  const maxDuration = preset && playLimit(preset, segment);
  const speed = segment?.speed ?? 1;
  const max = maxDuration === undefined ? Infinity : maxFrames(maxDuration, fps, speed);
  const newDuration = preset?.maxDurationSec;
  return {
    projectId,
    source,
    segments,
    segment,
    fps,
    total,
    maxDuration,
    newDuration,
    bounds: { total, max },
  };
}

/** The stop hint shows while its edge still sits at the limit (a faster speed lets it go). */
export function stopShown({ segment, bounds }: TimelineInfo, stop: Stop | undefined) {
  if (!stop || !segment || stop.segmentId !== segment.id) return undefined;
  return moveEdge(segment, stop.edge, stop.wanted, bounds).limited ? stop : undefined;
}

/** The segment plays longer than the limit: its speed was lowered after the trim. What can fix
 *  it: a faster speed (if even 4× is enough) or a shorter window. */
export function overLimit({ segment, fps, total, maxDuration, bounds }: TimelineInfo) {
  if (!segment || maxDuration === undefined || frameCount(segment) <= bounds.max) return undefined;
  const fits = frameCount(segment) / fps / SPEED_MAX <= maxDuration + 1e-9;
  return {
    speed: fits ? fitSpeed(segment, fps, maxDuration) : undefined,
    range: shorten(segment, bounds.max, total),
  };
}

/* Commands of the timeline: the hotkeys of R16 and the buttons call the same ones. */

/** ← and →: one frame, playback stops so the frame stays on the canvas. */
export function stepFrame({ fps, total }: TimelineInfo, delta: number) {
  const { time, setTime, setPlaying } = useEditor.getState();
  setPlaying(false);
  setTime(stepTime(time, delta, fps, total));
}

/** I and O: the edge goes to the playhead's frame, or stops at the limit with the hint. */
export function markEdge({ segment, fps, bounds }: TimelineInfo, edge: Edge) {
  if (!segment) return;
  const { time, setStop } = useEditor.getState();
  const wanted = frameAt(time, fps);
  const moved = moveEdge(segment, edge, wanted, bounds, true);
  setEdges(segment, moved);
  setStop(moved.limited ? { segmentId: segment.id, edge, wanted } : undefined);
}

/** One edit, so one step of history: a whole drag of a handle lands here. */
export function setEdges(segment: Segment, edges: Pick<Segment, "inFrame" | "outFrame">) {
  if (edges.inFrame === segment.inFrame && edges.outFrame === segment.outFrame) return;
  editSegment((s) => {
    s.inFrame = edges.inFrame;
    s.outFrame = edges.outFrame;
  });
}

/** «Вписать целиком» on the hint: the edge where it was pushed, the speed raised to fit. */
export function fitStop({ segment, fps, total, maxDuration }: TimelineInfo) {
  const { stop, setStop } = useEditor.getState();
  if (!segment || !stop || maxDuration === undefined) return;
  const fit = fitWhole(segment, stop.edge, stop.wanted, fps, maxDuration, total);
  editSegment((s) => {
    s.inFrame = fit.inFrame;
    s.outFrame = fit.outFrame;
    s.speed = fit.speed;
  });
  setStop(undefined);
}

/** «+ Отрезок» and «Сделать отрезком»: a new segment from the selection or the playhead. */
export function addSegment({ source, segments, segment, fps, total, newDuration }: TimelineInfo) {
  const { time, selection, edit, select, setTime, setSelection } = useEditor.getState();
  const max = newDuration === undefined ? Infinity : maxFrames(newDuration, fps, 1);
  const range = newRange(frameAt(time, fps), selection, { total, max });
  const name = t("editor.segment.name", { n: nextNumber(segments.map((s) => s.name)) });
  const created = newSegment(segment, source, range, crypto.randomUUID(), name);
  edit((scene) => void scene.segments.push(created));
  select(created.id);
  setSelection(undefined);
  setTime(range.inFrame / fps);
}

/** The cross on a segment's tab: gone at once, with «Вернуть» in the popup (artboard 8: what
 *  can be undone is done without asking). The last one may go too; export then waits. */
export function deleteSegment({ segments }: TimelineInfo, id: string) {
  const index = segments.findIndex((s) => s.id === id);
  const removed = segments[index];
  if (!removed) return;
  const { segmentId, edit, select, setTime } = useEditor.getState();
  const next = segments[index + 1] ?? segments[index - 1];
  edit((scene) => void scene.segments.splice(index, 1));
  if (segmentId === id) select(next?.id);

  notify.toast({
    tone: "info",
    title: t("editor.segment.deleted", { name: removed.name }),
    action: {
      label: t("editor.segment.restore"),
      onClick: () => {
        const state = useEditor.getState();
        if (state.scene?.segments.some((s) => s.id === removed.id)) return;
        state.edit((scene) => {
          scene.segments.splice(Math.min(index, scene.segments.length), 0, removed);
        });
        select(removed.id);
        if (state.source) setTime(removed.inFrame / fpsOf(state.source));
      },
    },
  });
}

/** «Ускорить до 1,25×» on the over-limit note: the whole window fits at the new speed. */
export function speedUp(speed: number) {
  editSegment((s) => void (s.speed = speed));
}
