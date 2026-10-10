import { useAppInfo } from "../../../shared/api/queries";
import { notify } from "../../../shared/notify";
import type { Segment, SourceInfo } from "../../../shared/api/types";
import { t } from "../../../shared/strings/ru";
import {
  fitWhole,
  frameAt,
  maxFrames,
  moveEdge,
  newRange,
  newSegment,
  nextNumber,
  stepTime,
} from "../model/timeline";
import type { Bounds, Edge } from "../model/timeline";
import { fpsOf } from "../model/frame";
import { editSegment, useEditor, useSegment } from "../model/store";

/** What every part of the timeline works with: the selected segment and its frame bounds. */
export interface TimelineInfo {
  projectId: string;
  source: SourceInfo;
  segments: Segment[];
  segment: Segment;
  fps: number;
  /** Frames in the source */
  total: number;
  /** The preset's limit, seconds; unknown until GET /api/v1/app answers */
  maxDuration?: number;
  bounds: Bounds;
}

export function useTimeline(): TimelineInfo | undefined {
  const projectId = useEditor((s) => s.projectId);
  const source = useEditor((s) => s.source);
  const segments = useEditor((s) => s.scene?.segments);
  const segment = useSegment();
  const limits = useAppInfo().data?.limits;
  if (!projectId || !source || !segments || !segment) return undefined;

  const fps = fpsOf(source);
  const total = Math.max(1, source.frameCount);
  const maxDuration = limits?.[segment.preset].maxDurationSec;
  const max = maxDuration === undefined ? Infinity : maxFrames(maxDuration, fps, segment.speed);
  return { projectId, source, segments, segment, fps, total, maxDuration, bounds: { total, max } };
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
  const { time, setStop } = useEditor.getState();
  const wanted = frameAt(time, fps);
  const moved = moveEdge(segment, edge, wanted, bounds, true);
  setEdges(segment, moved);
  setStop(moved.limited ? { segmentId: segment.id, edge, wanted } : undefined);
}

/** One edit, so one step of history once undo comes: a whole drag of a handle lands here. */
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
  if (!stop || maxDuration === undefined) return;
  const fit = fitWhole(segment, stop.edge, stop.wanted, fps, maxDuration, total);
  editSegment((s) => {
    s.inFrame = fit.inFrame;
    s.outFrame = fit.outFrame;
    s.speed = fit.speed;
  });
  setStop(undefined);
}

/** «+ Отрезок» and «Сделать отрезком»: a new segment from the selection or the playhead. */
export function addSegment({ segments, segment, fps, total, maxDuration }: TimelineInfo) {
  const { time, selection, edit, select, setTime, setSelection } = useEditor.getState();
  const max = maxDuration === undefined ? Infinity : maxFrames(maxDuration, fps, 1);
  const range = newRange(frameAt(time, fps), selection, { total, max });
  const name = t("editor.segment.name", { n: nextNumber(segments.map((s) => s.name)) });
  const created = newSegment(segment, range, crypto.randomUUID(), name);
  edit((scene) => void scene.segments.push(created));
  select(created.id);
  setSelection(undefined);
  setTime(range.inFrame / fps);
}

/** «Удалить отрезок»: gone at once, with «Вернуть» in the popup (artboard 8: what can be undone
 *  is done without asking). The last segment stays: a project always has one. */
export function deleteSegment({ segments }: TimelineInfo, id: string) {
  const index = segments.findIndex((s) => s.id === id);
  const removed = segments[index];
  if (!removed || segments.length < 2) return;
  const { segmentId, edit, select, setTime } = useEditor.getState();
  const next = segments[index + 1] ?? segments[index - 1];
  edit((scene) => void scene.segments.splice(index, 1));
  if (segmentId === id && next) select(next.id);

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
