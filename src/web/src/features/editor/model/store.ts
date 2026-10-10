import { produce, type Draft } from "immer";
import { create } from "zustand";
import type { Project, Scene, Segment, SourceInfo } from "../../../shared/api/types";
import { fpsOf } from "./frame";
import type { Edge, Selection } from "./timeline";

/* The editor's own state: the scene being edited plus what the screen shows of it.
   Every change of the scene goes through edit(): undo/redo and autosave hook in there
   (plan, «Undo / redo», «Автосохранение»), so screens never set the scene directly. */

/** «По окну» or a scale of source pixels */
export type Zoom = "fit" | number;

/** An edge pushed against the 3,00 s stop: the hint with «Вписать целиком» (artboard 3b, B). */
export interface Stop {
  segmentId: string;
  edge: Edge;
  /** The frame the edge was pushed to */
  wanted: number;
}

interface EditorState {
  projectId?: string;
  name: string;
  source?: SourceInfo;
  mediaUrl?: string;
  /** The scene's revision the server has; autosave sends it back */
  revision: number;
  scene?: Scene;
  /** The segment the inspector and the timeline work on */
  segmentId?: string;

  zoom: Zoom;
  /** «Рамка» on the glass bar: the whole source with the frame to drag; off, the sticker alone */
  cropTool: boolean;
  /** The selected segment's frame while it is dragged on the canvas; lands as one edit on release */
  frameDraft?: Segment["frame"];
  playing: boolean;
  /** Playhead, seconds of the source */
  time: number;
  /** The timeline's scale: 0 «Обзор», 1 «Кадры» at their widest */
  timelineZoom: number;
  /** Frames picked with click and Shift+click in the «Кадры» scale */
  selection?: Selection;
  stop?: Stop;

  load: (project: Project) => void;
  unload: () => void;
  edit: (recipe: (scene: Draft<Scene>) => void) => void;
  /** None: a project may have no segments, then there is nothing to export */
  select: (segmentId: string | undefined) => void;
  setZoom: (zoom: Zoom) => void;
  setCropTool: (on: boolean) => void;
  setFrameDraft: (frame?: Segment["frame"]) => void;
  setPlaying: (playing: boolean) => void;
  setTime: (time: number) => void;
  setTimelineZoom: (zoom: number) => void;
  setSelection: (selection?: Selection) => void;
  setStop: (stop?: Stop) => void;
}

const empty = {
  projectId: undefined,
  name: "",
  source: undefined,
  mediaUrl: undefined,
  revision: 0,
  scene: undefined,
  segmentId: undefined,
  zoom: "fit" as Zoom,
  cropTool: false,
  frameDraft: undefined,
  playing: false,
  time: 0,
  timelineZoom: 0,
  selection: undefined,
  stop: undefined,
};

export const useEditor = create<EditorState>()((set) => ({
  ...empty,
  load: (project) =>
    set({
      ...empty,
      projectId: project.id,
      name: project.name,
      source: project.source,
      mediaUrl: project.mediaUrl,
      revision: project.revision,
      scene: project.scene,
      segmentId: project.scene.segments[0]?.id,
      time: (project.scene.segments[0]?.inFrame ?? 0) / fpsOf(project.source),
    }),
  unload: () => set(empty),
  edit: (recipe) => set((s) => (s.scene ? { scene: produce(s.scene, recipe) } : {})),
  select: (segmentId) =>
    set({ segmentId, selection: undefined, stop: undefined, frameDraft: undefined }),
  setZoom: (zoom) => set({ zoom }),
  setCropTool: (cropTool) => set({ cropTool }),
  setFrameDraft: (frameDraft) => set({ frameDraft }),
  setPlaying: (playing) => set({ playing }),
  setTime: (time) => set({ time }),
  setTimelineZoom: (timelineZoom) => set({ timelineZoom }),
  setSelection: (selection) => set({ selection }),
  setStop: (stop) => set({ stop }),
}));

/** The selected segment; undefined before a project is loaded or when it has none. */
export function useSegment(): Segment | undefined {
  return useEditor((s) => s.scene?.segments.find((seg) => seg.id === s.segmentId));
}

/** Changes the selected segment: one edit, one step of history later. */
export function editSegment(recipe: (segment: Draft<Segment>) => void) {
  const { segmentId, edit } = useEditor.getState();
  edit((scene) => {
    const segment = scene.segments.find((s) => s.id === segmentId);
    if (segment) recipe(segment);
  });
}
