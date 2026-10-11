import { create } from "zustand";
import { api, call } from "../../../shared/api/client";
import type { Scene } from "../../../shared/api/types";
import { notifyProblem, problemText, toProblem, type ToastHandle } from "../../../shared/notify";
import { t } from "../../../shared/strings/ru";
import { useEditor } from "./store";

/* Autosave (plan, «Автосохранение»): a second after the last edit the scene goes to the server
   with the revision it was based on. Saves go one at a time, so an export that needs the scene
   saved waits for the one on its way instead of racing it to a 409. A failed save shows one
   popup, «Не сохранено» stays in the header and a new try goes every 30 s (artboard 6, state 4).
   A revision someone else saved (409) is not retried: it would fail the same way forever. */

export const SAVE_DELAY_MS = 1000;
export const RETRY_MS = 30_000;

export type SaveState = "saved" | "saving" | "error";

interface SaveStatusState {
  state: SaveState;
  /** What the popover of «Не сохранено» says */
  detail?: string;
}

export const useSave = create<SaveStatusState>()(() => ({ state: "saved" }));

export interface Saved {
  revision: number;
  savedAt: string;
}

export interface AutosaveOptions {
  /** PUT /projects/{id}/scene; tests put a fake here */
  send?: (projectId: string, revision: number, scene: Scene) => Promise<Saved>;
  /** The scene is on the server: the project's cached copy follows, so reopening it shows it */
  onSaved?: (projectId: string, scene: Scene, saved: Saved) => void;
}

/** The project autosave works for: what the server has of it. */
interface Tracked {
  projectId?: string;
  /** The scene object last saved; Immer gives every edit a new one, so !== means unsaved */
  saved?: Scene;
  revision: number;
  conflict: boolean;
}

const sendScene = (id: string, revision: number, scene: Scene) =>
  call(
    api.PUT("/api/v1/projects/{id}/scene", { params: { path: { id } }, body: { revision, scene } }),
  );

let options: Required<AutosaveOptions> = { send: sendScene, onSaved: () => {} };
let tracked: Tracked = { revision: 0, conflict: false };
let queue: Promise<unknown> = Promise.resolve();
let timer: ReturnType<typeof setTimeout> | undefined;
let retry: ReturnType<typeof setTimeout> | undefined;
let toast: ToastHandle | undefined;

function trackedOf(): Tracked {
  const { projectId, scene, revision } = useEditor.getState();
  return { projectId, saved: scene, revision, conflict: false };
}

function clearTimers() {
  clearTimeout(timer);
  clearTimeout(retry);
  timer = retry = undefined;
}

/** One save at a time, each starting from the state as it is when its turn comes. */
function enqueue<T>(job: () => Promise<T>): Promise<T> {
  const next = queue.then(job, job);
  queue = next.catch(() => undefined);
  return next;
}

async function send(target: Tracked, scene: Scene) {
  if (!target.projectId) return;
  const saved = await options.send(target.projectId, target.revision, scene);
  target.saved = scene;
  target.revision = saved.revision;
  // The project may have been closed and opened again meanwhile: the new session saves on top.
  if (tracked.projectId === target.projectId) tracked.revision = saved.revision;
  if (useEditor.getState().projectId === target.projectId) {
    useEditor.setState({ revision: saved.revision });
  }
  options.onSaved(target.projectId, scene, saved);
}

function succeeded() {
  clearTimeout(retry);
  retry = undefined;
  toast?.close();
  toast = undefined;
  const unsaved = useEditor.getState().scene !== tracked.saved;
  useSave.setState({ state: unsaved ? "saving" : "saved", detail: undefined });
}

function failed(error: unknown) {
  const problem = toProblem(error);
  tracked.conflict = problem.code === "revision_conflict";
  const { title, text } = problemText(problem);
  const detail = `${title}. ${tracked.conflict ? text : t("editor.save.retry")}`;
  // One popup for one outage: the retries that fail after it only keep the label.
  if (useSave.getState().state !== "error") toast = notifyProblem(error, { key: "autosave" });
  useSave.setState({ state: "error", detail });
  if (!tracked.conflict) {
    clearTimeout(retry);
    retry = setTimeout(() => void flushSave().catch(() => undefined), RETRY_MS);
  }
}

/** Saves the open project now if it has unsaved edits; resolves with the revision the server
 *  has. Export and publish go by a saved revision (contract, «Фоновые задачи»). */
export function flushSave(): Promise<number> {
  clearTimeout(timer);
  timer = undefined;
  return enqueue(async () => {
    const target = tracked;
    const { projectId, scene } = useEditor.getState();
    if (!projectId || !scene || projectId !== target.projectId)
      throw new Error("No project is open");
    if (scene === target.saved) return target.revision;
    if (target.conflict) throw new Error("The project was saved elsewhere");
    try {
      await send(target, scene);
    } catch (error) {
      if (target === tracked) failed(error);
      throw error;
    }
    if (target === tracked) succeeded();
    return target.revision;
  });
}

function edited() {
  if (tracked.conflict) return;
  if (useSave.getState().state === "saved") useSave.setState({ state: "saving" });
  clearTimeout(timer);
  timer = setTimeout(() => void flushSave().catch(() => undefined), SAVE_DELAY_MS);
}

/** Another project is open (or none): the edits of the one left still go to the server. */
function switched(left: Tracked, leftScene: Scene | undefined) {
  clearTimers();
  if (left.projectId && leftScene && leftScene !== left.saved && !left.conflict) {
    void enqueue(() => send(left, leftScene)).catch((error: unknown) =>
      notifyProblem(error, { key: "autosave" }),
    );
  }
  toast?.close();
  toast = undefined;
  tracked = trackedOf();
  useSave.setState({ state: "saved", detail: undefined });
}

/** Watches the editor's scene until the returned function is called. */
export function startAutosave(given: AutosaveOptions = {}): () => void {
  options = { send: given.send ?? sendScene, onSaved: given.onSaved ?? (() => {}) };
  tracked = trackedOf();
  useSave.setState({ state: "saved", detail: undefined });
  const unsubscribe = useEditor.subscribe((state, prev) => {
    if (state.projectId !== tracked.projectId) switched(tracked, prev.scene);
    else if (state.scene !== prev.scene) edited();
  });
  return () => {
    unsubscribe();
    clearTimers();
  };
}
