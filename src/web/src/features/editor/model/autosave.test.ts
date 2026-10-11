import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createDb } from "../../../mocks/data";
import { problemOf } from "../../../mocks/route";
import { ApiError } from "../../../shared/api/client";
import type { ErrorCode, Project, Scene } from "../../../shared/api/types";
import { useToasts } from "../../../shared/notify";
import { flushSave, RETRY_MS, SAVE_DELAY_MS, startAutosave, useSave, type Saved } from "./autosave";
import { editSegment, useEditor } from "./store";

// «Кот и клавиатура», revision 3
const demo = (): Project => structuredClone(createDb("ok").projects[0] as Project);
const other = (): Project => structuredClone(createDb("ok").projects[1] as Project);

const failure = (code: ErrorCode) => {
  const problem = problemOf(code);
  return new ApiError(problem.status, problem);
};

interface Call {
  projectId: string;
  revision: number;
  scene: Scene;
}

/** A server that takes saves in order; `fail` makes the next ones fail. */
function fakeServer() {
  const calls: Call[] = [];
  const failures: ErrorCode[] = [];
  let revision = 3;
  const send = vi.fn(async (projectId: string, base: number, scene: Scene): Promise<Saved> => {
    calls.push({ projectId, revision: base, scene });
    const code = failures.shift();
    if (code) throw failure(code);
    revision = base + 1;
    return { revision, savedAt: "2026-10-11T10:00:00Z" };
  });
  return { calls, send, fail: (...codes: ErrorCode[]) => failures.push(...codes) };
}

let stop: () => void = () => {};
let server: ReturnType<typeof fakeServer>;
const onSaved = vi.fn();

beforeEach(() => {
  vi.useFakeTimers();
  useEditor.getState().unload();
  useToasts.setState({ toasts: [] });
  server = fakeServer();
  onSaved.mockReset();
  stop = startAutosave({ send: server.send, onSaved });
  useEditor.getState().load(demo());
});

afterEach(() => {
  stop();
  vi.useRealTimers();
});

const state = () => useSave.getState().state;
const toasts = () => useToasts.getState().toasts;

describe("autosave", () => {
  it("sends the scene a second after the last edit, with the revision it was based on", async () => {
    editSegment((s) => void (s.speed = 2));
    expect(state()).toBe("saving");
    await vi.advanceTimersByTimeAsync(SAVE_DELAY_MS - 1);
    editSegment((s) => void (s.loop = "pingpong"));
    await vi.advanceTimersByTimeAsync(SAVE_DELAY_MS - 1);
    expect(server.calls).toHaveLength(0);

    await vi.advanceTimersByTimeAsync(1);
    expect(server.calls).toHaveLength(1);
    expect(server.calls[0]?.revision).toBe(3);
    expect(server.calls[0]?.scene).toBe(useEditor.getState().scene);
    expect(state()).toBe("saved");
    expect(useEditor.getState().revision).toBe(4);
  });

  it("does not send a project that was only opened", async () => {
    await vi.advanceTimersByTimeAsync(SAVE_DELAY_MS * 5);
    expect(server.calls).toHaveLength(0);
    expect(state()).toBe("saved");
  });

  it("saves an undo like any other edit", async () => {
    editSegment((s) => void (s.speed = 2));
    await vi.advanceTimersByTimeAsync(SAVE_DELAY_MS);
    useEditor.getState().undo();
    await vi.advanceTimersByTimeAsync(SAVE_DELAY_MS);

    expect(server.calls).toHaveLength(2);
    expect(server.calls[1]?.revision).toBe(4);
    expect(server.calls[1]?.scene.segments[0]?.speed).toBe(1);
  });

  it("hands the saved scene to the project's cached copy", async () => {
    editSegment((s) => void (s.speed = 2));
    await vi.advanceTimersByTimeAsync(SAVE_DELAY_MS);
    expect(onSaved).toHaveBeenCalledWith(demo().id, useEditor.getState().scene, {
      revision: 4,
      savedAt: "2026-10-11T10:00:00Z",
    });
  });

  it("saves an edit made while the previous save was on its way, on top of it", async () => {
    let release: () => void = () => {};
    server.send.mockImplementationOnce(async (projectId, revision, scene) => {
      server.calls.push({ projectId, revision, scene });
      await new Promise<void>((resolve) => (release = resolve));
      return { revision: revision + 1, savedAt: "" };
    });
    editSegment((s) => void (s.speed = 2));
    await vi.advanceTimersByTimeAsync(SAVE_DELAY_MS);
    editSegment((s) => void (s.speed = 3));
    await vi.advanceTimersByTimeAsync(SAVE_DELAY_MS);
    expect(server.calls).toHaveLength(1);

    release();
    await vi.advanceTimersByTimeAsync(0);
    expect(server.calls).toHaveLength(2);
    expect(server.calls[1]?.revision).toBe(4);
    expect(state()).toBe("saved");
  });

  it("shows one popup and «Не сохранено» when a save fails, and tries again every 30 s", async () => {
    server.fail("save_failed", "save_failed");
    editSegment((s) => void (s.speed = 2));
    await vi.advanceTimersByTimeAsync(SAVE_DELAY_MS);

    expect(state()).toBe("error");
    expect(useSave.getState().detail).toContain("каждые 30 секунд");
    expect(toasts()).toHaveLength(1);
    expect(toasts()[0]?.title).toBe("Не получается сохранить проект");

    await vi.advanceTimersByTimeAsync(RETRY_MS);
    expect(server.calls).toHaveLength(2);
    expect(state()).toBe("error");
    expect(toasts()).toHaveLength(1);

    await vi.advanceTimersByTimeAsync(RETRY_MS);
    expect(server.calls).toHaveLength(3);
    expect(state()).toBe("saved");
    expect(toasts()).toHaveLength(0);
  });

  it("keeps «Не сохранено» on an edit made while saves fail", async () => {
    server.fail("save_failed", "save_failed");
    editSegment((s) => void (s.speed = 2));
    await vi.advanceTimersByTimeAsync(SAVE_DELAY_MS);
    editSegment((s) => void (s.speed = 3));

    expect(state()).toBe("error");
    await vi.advanceTimersByTimeAsync(SAVE_DELAY_MS);
    expect(server.calls).toHaveLength(2);
    expect(toasts()).toHaveLength(1);
  });

  it("stops at a revision someone else saved and says to reload", async () => {
    server.fail("revision_conflict");
    editSegment((s) => void (s.speed = 2));
    await vi.advanceTimersByTimeAsync(SAVE_DELAY_MS);

    expect(state()).toBe("error");
    expect(useSave.getState().detail).toContain("Обновите страницу");
    editSegment((s) => void (s.speed = 3));
    await vi.advanceTimersByTimeAsync(RETRY_MS * 2);
    expect(server.calls).toHaveLength(1);
  });

  it("saves the edits of a project closed within the second, then forgets it", async () => {
    editSegment((s) => void (s.speed = 2));
    useEditor.getState().unload();
    await vi.advanceTimersByTimeAsync(0);

    expect(server.calls).toHaveLength(1);
    expect(server.calls[0]?.projectId).toBe(demo().id);
    expect(server.calls[0]?.scene.segments[0]?.speed).toBe(2);
    await vi.advanceTimersByTimeAsync(SAVE_DELAY_MS);
    expect(server.calls).toHaveLength(1);
    expect(state()).toBe("saved");
  });

  it("starts the next project clean", async () => {
    server.fail("save_failed");
    editSegment((s) => void (s.speed = 2));
    await vi.advanceTimersByTimeAsync(SAVE_DELAY_MS);
    useEditor.getState().load(other());

    expect(state()).toBe("saved");
    await vi.advanceTimersByTimeAsync(RETRY_MS);
    expect(server.calls.map((c) => c.projectId)).toEqual([demo().id, demo().id]);
  });

  it("gives export the revision of a scene saved right now", async () => {
    editSegment((s) => void (s.speed = 2));
    const revision = await flushSave();

    expect(revision).toBe(4);
    expect(server.calls).toHaveLength(1);
    await vi.advanceTimersByTimeAsync(SAVE_DELAY_MS);
    expect(server.calls).toHaveLength(1);
    expect(await flushSave()).toBe(4);
  });
});
