import { beforeEach, describe, expect, it } from "vitest";
import { createDb } from "../../../mocks/data";
import type { Project } from "../../../shared/api/types";
import { editSegment, useEditor } from "./store";

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
