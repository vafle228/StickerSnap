// @vitest-environment happy-dom
import { act, cleanup, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { ApiError } from "../api/client";
import type { ErrorCode } from "../api/types";
import { setReportWindow, type ReportedError } from "../report";
import { forgetErrors } from "../report/errors";
import { NotifyHost } from "./NotifyHost";
import { useToasts } from "./notify";
import { notifyProblem } from "./problems";

const failed = (code: ErrorCode, reportable: boolean, log?: string) =>
  new ApiError(502, {
    type: "about:blank",
    status: 502,
    title: "Telegram answered 400",
    code,
    reportable,
    log,
    traceId: "0af7651916cd43dd8448eb211c80319c",
  });

describe("«Сообщить» on a popup (19·1)", () => {
  beforeEach(() => {
    forgetErrors();
    useToasts.setState({ toasts: [] });
  });
  afterEach(() => cleanup());

  it("follows «Повторить» on an error the user cannot fix, and opens the report", async () => {
    const opened: ReportedError[] = [];
    setReportWindow((error) => opened.push(error));
    const user = userEvent.setup();
    render(<NotifyHost />);

    act(() => {
      notifyProblem(failed("telegram_error", true), {
        action: { label: "Повторить", onClick: () => {} },
      });
    });

    const toast = screen.getByRole("alert");
    const buttons = within(toast)
      .getAllByRole("button")
      .map((b) => b.textContent);
    expect(buttons).toEqual(["Повторить", "Сообщить", ""]);
    const report = within(toast).getByRole("button", { name: "Сообщить об ошибке" });
    expect(report.getAttribute("title")).toBe("Сообщить об ошибке");

    await user.click(report);
    expect(opened).toEqual([
      expect.objectContaining({
        code: "telegram_error",
        title: "Telegram не принял запрос",
        traceId: "0af7651916cd43dd8448eb211c80319c",
        source: "server",
      }),
    ]);
    expect(useToasts.getState().toasts.every((t) => t.leaving)).toBe(true);
  });

  it("keeps «Подробности» beside it when there is a log to read", () => {
    render(<NotifyHost />);
    act(() => {
      notifyProblem(failed("render_failed", true, "Conversion failed!"));
    });

    const toast = screen.getByRole("alert");
    expect(within(toast).getByRole("button", { name: "Сообщить об ошибке" })).toBeTruthy();
    expect(within(toast).getByRole("button", { name: "Подробности" })).toBeTruthy();
  });

  it("is not there for an error the user fixes himself", () => {
    render(<NotifyHost />);
    act(() => {
      notifyProblem(failed("rate_limited", false));
    });

    expect(
      within(screen.getByRole("alert")).queryByRole("button", { name: /Сообщить/ }),
    ).toBeNull();
  });
});
