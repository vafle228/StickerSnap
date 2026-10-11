import { beforeEach, describe, expect, it, vi } from "vitest";
import { createDb } from "../../../mocks/data";
import type { Project, Segment } from "../../../shared/api/types";
import { editSegment, HISTORY_MAX, MERGE_MS, useEditor } from "./store";

// «Кот и клавиатура»: 30 fps, segments «Стикер 1» (0–89) and «Стикер 2» (120–179)
const demo = (): Project => structuredClone(createDb("ok").projects[0] as Project);

describe("editor store", () => {
  beforeEach(() => useEditor.getState().unload());

  it("opens a project on its first segment with the playhead on its I", () => {
    const project = demo();
    project.scene.segments.reverse();
    useEditor.getState().load(project);

    const s = useEditor.getState();
    expect(s.projectId).toBe(project.id);
    expect(s.revision).toBe(3);
    expect(s.segmentId).toBe("seg-2");
    expect(s.time).toBe(120 / 30);
  });

  it("forgets what the previous project had on screen", () => {
    useEditor.getState().load(demo());
    useEditor.setState({ zoom: 2, cropTool: true, playing: true, selection: { from: 1, to: 5 } });
    useEditor.getState().load(demo());

    const s = useEditor.getState();
    expect(s).toMatchObject({ zoom: "fit", cropTool: false, playing: false });
    expect(s.selection).toBeUndefined();
  });

  it("opens a project without segments with nothing selected", () => {
    const project = demo();
    project.scene.segments = [];
    useEditor.getState().load(project);

    expect(useEditor.getState().segmentId).toBeUndefined();
    expect(useEditor.getState().time).toBe(0);
  });

  it("drops the selection, the stop hint and the frame draft when another segment is picked", () => {
    useEditor.getState().load(demo());
    useEditor.setState({
      selection: { from: 3, to: 9 },
      stop: { segmentId: "seg-1", edge: "out", wanted: 120 },
      frameDraft: { mode: "crop", preset: "free", rect: { x: 0, y: 0, width: 10, height: 10 } },
    });
    useEditor.getState().select("seg-2");

    const s = useEditor.getState();
    expect(s.segmentId).toBe("seg-2");
    expect(s.selection).toBeUndefined();
    expect(s.stop).toBeUndefined();
    expect(s.frameDraft).toBeUndefined();
  });

  it("makes every edit a new scene and leaves the old one as it was, ready for undo", () => {
    useEditor.getState().load(demo());
    const before = useEditor.getState().scene;
    useEditor.getState().edit((scene) => void scene.segments.pop());

    const after = useEditor.getState().scene;
    expect(after).not.toBe(before);
    expect(before?.segments).toHaveLength(2);
    expect(after?.segments).toHaveLength(1);
    // Untouched parts are shared, not copied
    expect(after?.layers).toBe(before?.layers);
  });

  it("ignores an edit before a project is loaded", () => {
    useEditor.getState().edit((scene) => void scene.segments.pop());
    expect(useEditor.getState().scene).toBeUndefined();
  });

  it("changes only the selected segment", () => {
    useEditor.getState().load(demo());
    useEditor.getState().select("seg-2");
    editSegment((s) => void (s.speed = 2));

    const [first, second] = useEditor.getState().scene?.segments ?? [];
    expect(first?.speed).toBe(1);
    expect(second?.speed).toBe(2);
  });

  it("does nothing to the scene when no segment is selected", () => {
    useEditor.getState().load(demo());
    useEditor.getState().select(undefined);
    const before = useEditor.getState().scene;
    editSegment((s) => void (s.speed = 2));

    expect(useEditor.getState().scene?.segments.map((s) => s.speed)).toEqual([1, 1]);
    expect(useEditor.getState().scene).toEqual(before);
  });
});

describe("undo and redo", () => {
  beforeEach(() => {
    useEditor.getState().unload();
    useEditor.getState().load(demo());
  });

  const speeds = () => useEditor.getState().scene?.segments.map((s) => s.speed);
  const ids = () => useEditor.getState().scene?.segments.map((s) => s.id);

  it("takes an edit back and brings it again", () => {
    const loaded = useEditor.getState().scene;
    editSegment((s) => void (s.speed = 2));
    useEditor.getState().undo();

    expect(useEditor.getState().scene).toEqual(loaded);
    useEditor.getState().redo();
    expect(speeds()).toEqual([2, 1]);
  });

  it("starts with nothing to undo and keeps no step for an edit that changed nothing", () => {
    expect(useEditor.getState().past).toHaveLength(0);
    editSegment((s) => void (s.speed = 1));
    expect(useEditor.getState().past).toHaveLength(0);
  });

  it("forgets the steps undone once a new edit is made", () => {
    editSegment((s) => void (s.speed = 2));
    useEditor.getState().undo();
    editSegment((s) => void (s.loop = "pingpong"));

    expect(useEditor.getState().future).toHaveLength(0);
    useEditor.getState().redo();
    expect(speeds()).toEqual([1, 1]);
  });

  it("makes a drag of the speed slider one step", () => {
    for (const speed of [1.1, 1.2, 1.5, 2]) {
      editSegment((s) => void (s.speed = speed), { merge: "speed" });
    }
    expect(useEditor.getState().past).toHaveLength(1);
    useEditor.getState().undo();
    expect(speeds()).toEqual([1, 1]);
  });

  it("starts a new step for the next drag after a pause", () => {
    vi.useFakeTimers();
    try {
      editSegment((s) => void (s.speed = 2), { merge: "speed" });
      vi.advanceTimersByTime(MERGE_MS + 1);
      editSegment((s) => void (s.speed = 3), { merge: "speed" });
      useEditor.getState().undo();
      expect(speeds()).toEqual([2, 1]);
    } finally {
      vi.useRealTimers();
    }
  });

  it("does not merge a slider drag into another segment's", () => {
    editSegment((s) => void (s.speed = 2), { merge: "speed" });
    useEditor.getState().select("seg-2");
    editSegment((s) => void (s.speed = 3), { merge: "speed" });
    expect(useEditor.getState().past).toHaveLength(2);
  });

  it("shows the segment an undone edit changed, with the playhead left alone", () => {
    useEditor.getState().select("seg-2");
    editSegment((s) => void (s.speed = 2));
    useEditor.getState().select("seg-1");
    useEditor.setState({ time: 1.5 });
    useEditor.getState().undo();

    expect(useEditor.getState().segmentId).toBe("seg-2");
    expect(useEditor.getState().time).toBe(1.5);
  });

  it("brings a deleted segment back where it was and selects it", () => {
    useEditor.getState().edit((scene) => void scene.segments.splice(0, 1));
    useEditor.getState().select("seg-2");
    useEditor.getState().undo();

    expect(ids()).toEqual(["seg-1", "seg-2"]);
    expect(useEditor.getState().segmentId).toBe("seg-1");
  });

  it("selects a neighbour when undo takes away the selected segment", () => {
    const added = { ...structuredClone(useEditor.getState().scene?.segments[1]), id: "seg-3" };
    useEditor.getState().edit((scene) => void scene.segments.push(added as Segment));
    useEditor.getState().select("seg-3");
    useEditor.getState().undo();

    expect(ids()).toEqual(["seg-1", "seg-2"]);
    expect(useEditor.getState().segmentId).toBe("seg-1");
    useEditor.getState().redo();
    expect(useEditor.getState().segmentId).toBe("seg-3");
  });

  it("drops the stop hint and the frame selection made for the scene before", () => {
    editSegment((s) => void (s.outFrame = 60));
    useEditor.setState({
      selection: { from: 3, to: 9 },
      stop: { segmentId: "seg-1", edge: "out", wanted: 120 },
    });
    useEditor.getState().undo();

    expect(useEditor.getState().selection).toBeUndefined();
    expect(useEditor.getState().stop).toBeUndefined();
  });

  it("keeps the history of one project only", () => {
    editSegment((s) => void (s.speed = 2));
    useEditor.getState().load(demo());
    expect(useEditor.getState().past).toHaveLength(0);
  });

  it("keeps the last HISTORY_MAX steps", () => {
    for (let i = 1; i <= HISTORY_MAX + 5; i++) editSegment((s) => void (s.outFrame = i));
    expect(useEditor.getState().past).toHaveLength(HISTORY_MAX);
  });
});
