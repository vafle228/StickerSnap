import { applyPatches, enablePatches, produceWithPatches, type Draft, type Patch } from "immer";
import { create } from "zustand";
import type { Project, Scene, Segment, SourceInfo } from "../../../shared/api/types";
import { fpsOf } from "./frame";
import type { Edge, Selection } from "./timeline";

enablePatches();

/* The editor's own state: the scene being edited plus what the screen shows of it.
   Every change of the scene goes through edit(): it keeps the history for undo/redo, and
   autosave watches the scene it leaves (plan, «Undo / redo», «Автосохранение»), so screens
   never set the scene directly. */

/** «По окну» or a scale of source pixels */
export type Zoom = "fit" | number;

/** One step of history: the patches of an edit and the ones that take it back (plan, «Undo / redo»). */
export interface Step {
  patches: Patch[];
  inverse: Patch[];
  /** The segment selected when the edit was made: undo shows it again */
  segmentId?: string;
  /** Edits with the same key that follow each other within MERGE_MS are one step: a slider drag */
  merge?: string;
  /** Date.now() of the step's last edit */
  at: number;
}

export interface EditOptions {
  /** See Step.merge */
  merge?: string;
}

/** How many steps back undo goes; older ones are dropped. */
export const HISTORY_MAX = 200;
/** A pause longer than this ends a merged step: the next drag of the slider is a step of its own. */
export const MERGE_MS = 1000;

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
  /** The lock in «Растянуть»: the stretch's proportion is fixed, nothing on the canvas or in
   *  the inspector changes it */
  stretchLocked: boolean;
  playing: boolean;
  /** Playhead, seconds of the source */
  time: number;
  /** The timeline's scale: 0 «Обзор», 1 «Кадры» at their widest */
  timelineZoom: number;
  /** Frames picked with click and Shift+click in the «Кадры» scale */
  selection?: Selection;
  stop?: Stop;
  /** Undo goes back through these, newest last; history belongs to the open project */
  past: Step[];
  /** What undo took back, for redo; a new edit clears it */
  future: Step[];

  load: (project: Project) => void;
  unload: () => void;
  /** The one way to change the scene: every edit is a step of history and gets autosaved. */
  edit: (recipe: (scene: Draft<Scene>) => void, options?: EditOptions) => void;
  undo: () => void;
  redo: () => void;
  /** None: a project may have no segments, then there is nothing to export */
  select: (segmentId: string | undefined) => void;
  setZoom: (zoom: Zoom) => void;
  setCropTool: (on: boolean) => void;
  setFrameDraft: (frame?: Segment["frame"]) => void;
  setStretchLocked: (locked: boolean) => void;
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
  stretchLocked: false,
  playing: false,
  time: 0,
  timelineZoom: 0,
  selection: undefined,
  stop: undefined,
  past: [] as Step[],
  future: [] as Step[],
};

export const useEditor = create<EditorState>()((set, get) => ({
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
  edit: (recipe, options = {}) => {
    const { scene, segmentId, past } = get();
    if (!scene) return;
    const [next, patches, inverse] = produceWithPatches(scene, recipe);
    if (patches.length === 0) return;
    const at = Date.now();
    const last = past.at(-1);
    const merged =
      last &&
      options.merge !== undefined &&
      last.merge === options.merge &&
      last.segmentId === segmentId &&
      at - last.at < MERGE_MS;
    const step: Step = merged
      ? {
          ...last,
          patches: [...last.patches, ...patches],
          inverse: [...inverse, ...last.inverse],
          at,
        }
      : { patches, inverse, segmentId, merge: options.merge, at };
    set({
      scene: next,
      past: [...(merged ? past.slice(0, -1) : past), step].slice(-HISTORY_MAX),
      future: [],
    });
  },
  undo: () => {
    const { scene, past, future } = get();
    const step = past.at(-1);
    if (!scene || !step) return;
    const next = applyPatches(scene, step.inverse);
    set({
      scene: next,
      past: past.slice(0, -1),
      future: [...future, step],
      ...afterTravel(get(), scene, next, step),
    });
  },
  redo: () => {
    const { scene, past, future } = get();
    const step = future.at(-1);
    if (!scene || !step) return;
    const next = applyPatches(scene, step.patches);
    set({
      scene: next,
      past: [...past, { ...step, merge: undefined }],
      future: future.slice(0, -1),
      ...afterTravel(get(), scene, next, step),
    });
  },
  select: (segmentId) =>
    set({ segmentId, selection: undefined, stop: undefined, frameDraft: undefined }),
  setZoom: (zoom) => set({ zoom }),
  setCropTool: (cropTool) => set({ cropTool }),
  setFrameDraft: (frameDraft) => set({ frameDraft }),
  setStretchLocked: (stretchLocked) => set({ stretchLocked }),
  setPlaying: (playing) => set({ playing }),
  setTime: (time) => set({ time }),
  setTimelineZoom: (timelineZoom) => set({ timelineZoom }),
  setSelection: (selection) => set({ selection }),
  setStop: (stop) => set({ stop }),
}));

/** What the screen shows after undo or redo: the segment the step changed, without the hints
 *  and picks made for the scene as it was. The playhead stays where it is. */
function afterTravel(state: EditorState, before: Scene, after: Scene, step: Step) {
  const had = new Set(before.segments.map((s) => s.id));
  const has = (id: string | undefined) => after.segments.some((s) => s.id === id);
  // A segment that came back (an undone delete, a redone add) is what the user looks for.
  const back = after.segments.find((s) => !had.has(s.id));
  let segmentId = back?.id ?? (has(step.segmentId) ? step.segmentId : state.segmentId);
  if (!has(segmentId)) {
    // The selected one is gone: its neighbour takes its place, as after the cross on its tab.
    const index = before.segments.findIndex((s) => s.id === segmentId);
    segmentId = after.segments[Math.min(Math.max(index, 0), after.segments.length - 1)]?.id;
  }
  return { segmentId, selection: undefined, stop: undefined, frameDraft: undefined };
}

/** The selected segment; undefined before a project is loaded or when it has none. */
export function useSegment(): Segment | undefined {
  return useEditor((s) => s.scene?.segments.find((seg) => seg.id === s.segmentId));
}

/** Changes the selected segment: one edit, one step of history. */
export function editSegment(recipe: (segment: Draft<Segment>) => void, options?: EditOptions) {
  const { segmentId, edit } = useEditor.getState();
  edit((scene) => {
    const segment = scene.segments.find((s) => s.id === segmentId);
    if (segment) recipe(segment);
  }, options);
}
