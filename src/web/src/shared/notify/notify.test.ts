import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { EXIT_FALLBACK_MS } from "@stickersnap/ds/hooks";
import { ApiError } from "../api/client";
import { notify, useToasts } from "./notify";
import { notifyProblem, problemText, toProblem } from "./problems";

const titles = () =>
  useToasts
    .getState()
    .toasts.filter((t) => !t.leaving)
    .map((t) => t.title);

describe("notify", () => {
  beforeEach(() => useToasts.setState({ toasts: [] }));
  afterEach(() => vi.useRealTimers());

  it("keeps three toasts: a fourth pushes out the oldest info, never an error", () => {
    notify.toast({ tone: "danger", title: "error" });
    notify.toast({ tone: "info", title: "old info" });
    notify.toast({ tone: "success", title: "done" });
    notify.toast({ tone: "info", title: "new info" });

    expect(titles()).toEqual(["error", "done", "new info"]);
  });

  it("lets a closed toast leave before it is gone from the store", () => {
    vi.useFakeTimers();
    const toast = notify.toast({ tone: "info", title: "Скопировано" });
    toast.close();

    expect(useToasts.getState().toasts).toMatchObject([{ id: toast.id, leaving: true }]);
    vi.advanceTimersByTime(EXIT_FALLBACK_MS);
    expect(useToasts.getState().toasts).toEqual([]);
  });

  it("does not count a leaving toast among the three", () => {
    vi.useFakeTimers();
    notify.toast({ tone: "info", title: "first" });
    notify.toast({ tone: "info", title: "second" });
    notify.toast({ tone: "info", title: "third" });
    notify.toast({ tone: "info", title: "fourth" });
    notify.toast({ tone: "info", title: "fifth" });

    expect(titles()).toEqual(["third", "fourth", "fifth"]);
    expect(useToasts.getState().toasts.filter((t) => t.leaving)).toHaveLength(2);
  });

  it("turns a running task into its result in the same toast", () => {
    const job = notify.toast({ title: "Экспорт", progress: 40 });
    job.update({ tone: "success", title: "Готово", progress: undefined });

    const [toast] = useToasts.getState().toasts;
    expect(toast).toMatchObject({ id: job.id, tone: "success", title: "Готово" });
    expect(toast?.progress).toBeUndefined();
  });

  it("replaces a toast with the same key in place", () => {
    const first = notify.toast({ tone: "danger", title: "Не сохранено", key: "save" });
    const second = notify.toast({ tone: "danger", title: "Снова не сохранено", key: "save" });

    expect(second.id).toBe(first.id);
    expect(titles()).toEqual(["Снова не сохранено"]);
  });
});

describe("problems", () => {
  beforeEach(() => useToasts.setState({ toasts: [] }));

  const problem = (code: "format_unsupported" | "pack_name_taken") =>
    new ApiError(415, {
      type: "about:blank",
      status: 415,
      title: "Unsupported",
      code,
      params: { ext: "avi" },
      traceId: "t-1",
    });

  it("fills the text from params", () => {
    expect(problemText(toProblem(problem("format_unsupported"))).text).toContain(".avi");
  });

  it("shows a toast only for codes whose form is a popup", () => {
    expect(notifyProblem(problem("format_unsupported"))).toBeDefined();
    expect(notifyProblem(problem("pack_name_taken"))).toBeUndefined();
    expect(titles()).toHaveLength(1);
  });

  it("reads anything else as internal", () => {
    expect(toProblem(new TypeError("Failed to fetch")).code).toBe("internal");
    expect(toProblem(new ApiError(401)).code).toBe("internal");
  });
});
