// @vitest-environment happy-dom
import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { forgetErrors, type ReportedError } from "./errors";
import { setReportWindow } from "./reporter";
import { ScreenBoundary } from "./ScreenBoundary";

/** A section that throws while `broken.now` is set */
const broken = { now: true };
function Section() {
  if (broken.now) throw new TypeError("Cannot read properties of undefined (reading 'segments')");
  return <p>Проекты</p>;
}

describe("«Что-то сломалось» (19·3)", () => {
  beforeEach(() => {
    forgetErrors();
    broken.now = true;
    // React reports a caught render error on the console; the test does not need it
    vi.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it("stands in the section's place with the report first and the code under it", async () => {
    const opened: ReportedError[] = [];
    setReportWindow((error) => opened.push(error));
    const user = userEvent.setup();
    render(
      <ScreenBoundary resetKey="projects">
        <Section />
      </ScreenBoundary>,
    );

    const crash = await screen.findByRole("alert");
    expect(crash.textContent).toContain("Что-то сломалось");
    expect(crash.textContent).toContain("Проект сохранён автоматически, ничего не потеряно.");
    expect(crash.textContent).toMatch(/Код [0-9a-f]{4}-[0-9a-f]{4} · \d\d:\d\d/);

    await user.click(screen.getByRole("button", { name: "Сообщить об ошибке" }));
    expect(opened).toEqual([
      expect.objectContaining({
        code: "internal",
        source: "interface",
        message: "TypeError: Cannot read properties of undefined (reading 'segments')",
        stack: expect.stringContaining("Section"),
      }),
    ]);
  });

  it("draws the section again on «Перезапустить экран» and on another section", async () => {
    const user = userEvent.setup();
    const { rerender } = render(
      <ScreenBoundary resetKey="projects">
        <Section />
      </ScreenBoundary>,
    );
    await screen.findByRole("alert");

    broken.now = false;
    await user.click(screen.getByRole("button", { name: "Перезапустить экран" }));
    expect(screen.getByText("Проекты")).toBeTruthy();

    broken.now = true;
    rerender(
      <ScreenBoundary resetKey="packs">
        <Section />
      </ScreenBoundary>,
    );
    await screen.findByRole("alert");
    broken.now = false;
    rerender(
      <ScreenBoundary resetKey="settings">
        <Section />
      </ScreenBoundary>,
    );
    expect(screen.getByText("Проекты")).toBeTruthy();
  });

  it("shows nothing for a kept section that is not on screen", async () => {
    render(
      <ScreenBoundary resetKey="editor" hidden>
        <Section />
      </ScreenBoundary>,
    );
    await Promise.resolve();
    expect(screen.queryByRole("alert")).toBeNull();
  });
});
