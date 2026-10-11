// @vitest-environment happy-dom
import { act, render, renderHook, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { useMockServer, withProviders } from "../../test/dom";
import { useApp } from "../../app/store";
import { useToasts } from "../../shared/notify";
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
