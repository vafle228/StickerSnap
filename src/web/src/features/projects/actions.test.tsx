// @vitest-environment happy-dom
import { act, render, renderHook, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { server, useMockServer, withProviders } from "../../test/dom";
import { useApp } from "../../app/store";
import { useToasts } from "../../shared/notify";
import { OverlayHost } from "../../shared/overlays";
import { platform } from "../../shared/platform";
import { useEditor } from "../editor/model/store";
import { useProjectActions } from "./actions";
import { ReadingFile } from "./ReadingFile";

/* «Новый проект» and «Импорт видео» (R1, R21, artboard 3c). A path skips the upload, so the
   demo server reads the file by its name, as the desktop will. */

const video = { name: "Кот прыгает.mp4", path: "C:/video/Кот прыгает.mp4" };

function actions() {
  return renderHook(() => useProjectActions(), { wrapper: withProviders() }).result.current;
}

describe("import a video", () => {
  it("makes one project when the file is imported twice in a row", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const db = useMockServer("ok");
    const before = db.projects.map((p) => p.id);
    const { create } = actions();

    const first = create(video);
    const second = create(video);
    await vi.advanceTimersByTimeAsync(2000);
    await act(() => Promise.all([first, second]));

    const made = db.projects.filter((p) => !before.includes(p.id));
    expect(made).toHaveLength(1);
    expect(useApp.getState().projectId).toBe(made[0]?.id);
  });

  it("shows «Читаю» after 300 ms, and «Отменить» makes no project", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const db = useMockServer("ok");
    const before = db.projects.length;
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    render(<ReadingFile />);
    const { create } = actions();

    const done = create(video);
    expect(screen.queryByRole("status")).toBeNull();
    await act(() => vi.advanceTimersByTimeAsync(300));
    expect(screen.getByRole("status").textContent).toContain("Читаю «Кот прыгает.mp4»");

    await user.click(screen.getByRole("button", { name: "Отменить" }));
    await act(() => done);

    expect(screen.queryByRole("status")).toBeNull();
    expect(db.projects).toHaveLength(before);
    expect(useApp.getState().projectId).toBeUndefined();
  });

  it("names a broken file in an «import» toast with «Выбрать другой»", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    useMockServer("import-broken");
    const { create } = actions();

    const done = create(video);
    await vi.advanceTimersByTimeAsync(2000);
    await act(() => done);

    const [toast] = useToasts.getState().toasts;
    expect(toast?.key).toBe("import");
    expect(toast?.title).toBe("Не получилось открыть Кот прыгает.mp4");
    expect(toast?.action?.label).toBe("Выбрать другой");
  });
});

const FIRST = { id: "6f1d2c3a-1b2c-4d5e-8f90-0a1b2c3d4e51", name: "Кот и клавиатура" };

describe("rename a project", () => {
  it("sends nothing for an empty name or the same name", async () => {
    const db = useMockServer("ok");
    const sent = vi.fn();
    server.events.on("request:start", ({ request }) => {
      if (request.method === "PATCH") sent();
    });
    const { rename } = actions();

    await act(() => rename({ ...FIRST, path: "" }, "   "));
    await act(() => rename({ ...FIRST, path: "" }, " Кот и клавиатура "));

    expect(sent).not.toHaveBeenCalled();
    expect(db.projects.find((p) => p.id === FIRST.id)?.name).toBe(FIRST.name);
  });

  it("says the old name stayed when the disk is full", async () => {
    const db = useMockServer("project-errors");
    const { rename } = actions();

    await act(() => rename({ ...FIRST, path: "" }, "Кот"));

    const [toast] = useToasts.getState().toasts;
    expect(toast?.key).toBe("project-rename");
    expect(toast?.description).toBe("Нет места или доступа к папке. Осталось прежнее название.");
    expect(toast?.details?.log).toContain("not enough space");
    expect(db.projects.find((p) => p.id === FIRST.id)?.name).toBe(FIRST.name);
  });
});

describe("remove or delete a project", () => {
  it("closes a project removed from the list and brings it back with «Вернуть»", async () => {
    const db = useMockServer("ok");
    const project = db.projects.find((p) => p.id === FIRST.id);
    if (!project) throw new Error("no demo project");
    useApp.getState().openProject(FIRST.id);
    const { forget } = actions();

    await act(() => forget(project));

    expect(useApp.getState().projectId).toBeUndefined();
    expect(db.projects.some((p) => p.id === FIRST.id)).toBe(false);
    const [toast] = useToasts.getState().toasts;
    expect(toast?.title).toBe("Проект убран из списка");
    expect(toast?.action?.label).toBe("Вернуть");

    await act(async () => toast?.action?.onClick());
    await vi.waitFor(() => expect(db.projects.some((p) => p.id === FIRST.id)).toBe(true));
  });

  it("deletes from disk only after «Удалить» in the confirmation", async () => {
    const db = useMockServer("ok");
    const project = db.projects.find((p) => p.id === FIRST.id);
    if (!project) throw new Error("no demo project");
    const user = userEvent.setup();
    render(<OverlayHost />);
    const { trash } = actions();

    let done = trash(project);
    await user.click(await screen.findByRole("button", { name: "Отмена" }));
    await act(() => done);
    expect(db.projects.some((p) => p.id === FIRST.id)).toBe(true);

    done = trash(project);
    const dialog = await screen.findByRole("alertdialog", {
      name: "Удалить «Кот и клавиатура» с диска?",
    });
    await user.click(within(dialog).getByRole("button", { name: "Удалить" }));
    await act(() => done);
    expect(db.projects.some((p) => p.id === FIRST.id)).toBe(false);
    expect(db.forgotten.some((p) => p.id === FIRST.id)).toBe(false);
  });
});

describe("pick a new source", () => {
  it("warns about a segment past the end, and «Показать» opens the project on it", async () => {
    const db = useMockServer("source-missing");
    const project = db.projects[0];
    const last = project?.scene.segments.at(-1);
    if (!project || !last) throw new Error("no demo project");
    vi.spyOn(platform, "pickVideo").mockResolvedValue(video);
    const { replaceSource } = actions();

    await act(() => replaceSource(project.id));

    const [toast] = useToasts.getState().toasts;
    expect(toast?.title).toBe(`«${last.name}» выходит за конец видео`);
    act(() => toast?.action?.onClick());
    expect(useApp.getState()).toMatchObject({ section: "editor", projectId: project.id });
    // The editor loads the project and lands on that segment, not on the first one
    act(() => useEditor.getState().load(project));
    expect(useEditor.getState().segmentId).toBe(last.id);
  });
});
