// @vitest-environment happy-dom
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { ApiError } from "../api/client";
import { useToasts } from "../notify/notify";
import { crashError, notifyCrash, startCapture, uncaught } from "./capture";
import { forgetErrors } from "./errors";
import { trail } from "./trail";

const toasts = () => useToasts.getState().toasts;

describe("what the interface catches by itself", () => {
  let stop: () => void = () => {};

  beforeEach(() => {
    forgetErrors();
    trail.clear();
    useToasts.setState({ toasts: [] });
    stop = startCapture();
  });

  afterEach(() => stop());

  it("puts each click on something pressable on the trail", () => {
    document.body.innerHTML = `<button>Экспорт</button><p>текст</p>`;
    document.querySelector("button")?.click();
    document.querySelector("p")?.click();

    expect(trail.list().map((s) => s.what)).toEqual(["Кнопка: Экспорт"]);
  });

  it("shows an error nothing caught once, with «Сообщить» and a count", () => {
    const error = new TypeError("segment is undefined");
    window.dispatchEvent(new ErrorEvent("error", { error, message: error.message }));
    window.dispatchEvent(new ErrorEvent("error", { error, message: error.message }));

    expect(toasts()).toHaveLength(1);
    expect(toasts()[0]).toMatchObject({
      tone: "danger",
      title: "Что-то пошло не так",
      report: { source: "interface", message: "TypeError: segment is undefined", count: 2 },
    });
  });

  it("lets the browser's noise and an expected server's error pass", () => {
    expect(
      uncaught(new Error("ResizeObserver loop completed with undelivered notifications")),
    ).toBe(undefined);
    const expected = new ApiError(503, {
      type: "about:blank",
      status: 503,
      title: "No network",
      code: "network_unavailable",
      reportable: false,
      traceId: "t-1",
    });
    expect(uncaught(expected)).toBeUndefined();
    expect(uncaught("plain text")).toMatchObject({ message: "plain text", code: "internal" });
  });
});

describe("the crash of the last launch", () => {
  beforeEach(() => {
    forgetErrors();
    useToasts.setState({ toasts: [] });
  });

  it("says the server stopped, with its code, and offers to report its output", () => {
    notifyCrash({ kind: "server", at: "2026-10-19T12:00:00Z", code: 3, output: "IOException" });

    expect(toasts()[0]).toMatchObject({
      title: "Прошлый запуск завершился ошибкой",
      description: expect.stringContaining("(код 3)"),
      report: {
        source: "shell",
        code: "internal",
        message: "The server stopped with code 3",
        stack: "IOException",
        at: "2026-10-19T12:00:00.000Z",
      },
    });
  });

  it("names a dead window and a shell that never quit", () => {
    expect(crashError({ kind: "window", at: "2026-10-19T12:00:00Z", reason: "oom" })).toMatchObject(
      {
        title: "Окно перезапустилось после сбоя",
        message: "The window's renderer is gone: oom",
      },
    );
    expect(crashError({ kind: "shell", at: "2026-10-19T11:00:00Z" }).message).toBe(
      "The app ended without quitting",
    );
  });
});
