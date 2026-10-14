import { beforeEach, describe, expect, it } from "vitest";
import { createDb } from "../../../mocks/data";
import type { Project } from "../../../shared/api/types";
import { useToasts } from "../../../shared/notify/notify";
import { fpsOf } from "../model/frame";
import { maxFrames } from "../model/timeline";
import { useEditor } from "../model/store";
import {
  addSegment,
  deleteSegment,
  fitStop,
  markEdge,
  overLimit,
  setEdges,
  stepFrame,
  stopShown,
  type TimelineInfo,
} from "./useTimeline";

/* The timeline's commands against the real editor store: what I, O, ←, →, «+ Отрезок»,
   the cross on a tab and «Вписать целиком» do to the scene. */

// «Кот и клавиатура»: 240 frames at 30 fps, «Стикер 1» 0–89 and «Стикер 2» 120–179
const demo = (): Project => structuredClone(createDb("ok").projects[0] as Project);
const MAX_SEC = 3;

/** What useTimeline() gives the components, read from the store as it is now. */
function info(): TimelineInfo {
  const s = useEditor.getState();
  if (!s.projectId || !s.source || !s.scene) throw new Error("no project");
  const segment = s.scene.segments.find((seg) => seg.id === s.segmentId);
  const fps = fpsOf(s.source);
  const total = s.source.frameCount;
  const max = maxFrames(MAX_SEC, fps, segment?.speed ?? 1);
  return {
    projectId: s.projectId,
    source: s.source,
    segments: s.scene.segments,
    segment,
    fps,
    total,
    maxDuration: MAX_SEC,
    newDuration: MAX_SEC,
    bounds: { total, max },
  };
}

const segments = () => useEditor.getState().scene?.segments ?? [];
const at = (frame: number) => useEditor.getState().setTime(frame / 30);
const setSpeed = (index: number, speed: number) =>
  useEditor.getState().edit((scene) => {
    const segment = scene.segments[index];
    if (segment) segment.speed = speed;
  });

describe("timeline commands", () => {
  beforeEach(() => {
    useToasts.setState({ toasts: [] });
    useEditor.getState().load(demo());
  });

  it("stops O at 3,00 s and shows the hint with the frame it was pushed to", () => {
    at(120);
    markEdge(info(), "out");

    expect(segments()[0]).toMatchObject({ inFrame: 0, outFrame: 89 });
    const stop = useEditor.getState().stop;
    expect(stop).toEqual({ segmentId: "seg-1", edge: "out", wanted: 120 });
    expect(stopShown(info(), stop)).toBe(stop);
  });

  it("hides the hint once a faster speed lets the edge go", () => {
    at(120);
    markEdge(info(), "out");
    setSpeed(0, 2);

    expect(stopShown(info(), useEditor.getState().stop)).toBeUndefined();
  });

  it("lets I pressed past O take O along", () => {
    useEditor.getState().select("seg-2");
    at(200);
    markEdge(info(), "in");

    expect(segments()[1]).toMatchObject({ inFrame: 200, outFrame: 200 });
    expect(useEditor.getState().stop).toBeUndefined();
  });

  it("«Вписать целиком» moves the edge where it was pushed and raises the speed", () => {
    at(120);
    markEdge(info(), "out");
    fitStop(info());

    // 121 frames at 30 fps fit 3 s at 1,35× (1,344… rounded up)
    expect(segments()[0]).toMatchObject({ inFrame: 0, outFrame: 120, speed: 1.35 });
    expect(useEditor.getState().stop).toBeUndefined();
  });

  it("does not make an edit when the edges stay where they are", () => {
    const [first] = segments();
    if (!first) throw new Error("no segment");
    const before = useEditor.getState().scene;
    setEdges(first, { inFrame: 0, outFrame: 89 });
    expect(useEditor.getState().scene).toBe(before);
  });

  it("offers a faster speed and a shorter window when a slower speed broke the limit", () => {
    setSpeed(0, 0.5);

    expect(overLimit(info())).toEqual({ speed: 1, range: { inFrame: 0, outFrame: 44 } });
  });

  it("says nothing about the limit while the segment fits", () => {
    expect(overLimit(info())).toBeUndefined();
  });

  it("steps one frame and stops playback", () => {
    useEditor.setState({ playing: true });
    at(10);
    stepFrame(info(), 1);

    expect(useEditor.getState().playing).toBe(false);
    expect(useEditor.getState().time).toBeCloseTo(11 / 30);
  });

  it("adds «Стикер 3» from the playhead, selects it and puts the playhead on its I", () => {
    at(200);
    addSegment(info());

    const added = segments()[2];
    expect(added).toMatchObject({ name: "Стикер 3", inFrame: 200, outFrame: 239, speed: 1 });
    expect(useEditor.getState().segmentId).toBe(added?.id);
    expect(useEditor.getState().time).toBeCloseTo(200 / 30);
  });

  it("makes a segment of the picked frames and forgets the pick", () => {
    useEditor.getState().setSelection({ from: 10, to: 20 });
    addSegment(info());

    expect(segments()[2]).toMatchObject({ inFrame: 10, outFrame: 20 });
    expect(useEditor.getState().selection).toBeUndefined();
  });

  it("deletes a segment at once and brings it back in its place from the popup", () => {
    deleteSegment(info(), "seg-1");

    expect(segments().map((s) => s.id)).toEqual(["seg-2"]);
    expect(useEditor.getState().segmentId).toBe("seg-2");

    const [toast] = useToasts.getState().toasts;
    expect(toast?.title).toBe("«Стикер 1» удалён");
    toast?.action?.onClick();
    // A second click on «Вернуть» does not make a copy
    toast?.action?.onClick();

    expect(segments().map((s) => s.id)).toEqual(["seg-1", "seg-2"]);
    expect(useEditor.getState().segmentId).toBe("seg-1");
  });

  it("lets the last segment go, leaving nothing selected", () => {
    deleteSegment(info(), "seg-1");
    deleteSegment(info(), "seg-2");

    expect(segments()).toEqual([]);
    expect(useEditor.getState().segmentId).toBeUndefined();
  });
});
