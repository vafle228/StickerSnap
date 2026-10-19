import { beforeEach, describe, expect, it } from "vitest";
import type { Problem } from "../api/types";
import { captureError, captureProblem, forgetErrors, REPEAT_MS, shortNumber } from "./errors";

const at = (ms: number) => new Date(Date.UTC(2026, 9, 19, 13, 22) + ms);

const uiError = {
  code: "internal" as const,
  title: "Что-то сломалось",
  source: "interface" as const,
  message: "TypeError: segment is undefined",
};

const problem = (over: Partial<Problem> = {}): Problem => ({
  type: "about:blank",
  status: 502,
  title: "Telegram answered 400",
  code: "telegram_error",
  reportable: true,
  traceId: "0af7651916cd43dd8448eb211c80319c",
  ...over,
});

describe("the errors a user may report", () => {
  beforeEach(() => forgetErrors());

  it("makes one report of the same error within 10 minutes, counting it", () => {
    const first = captureError(uiError, at(0));
    const again = captureError(uiError, at(REPEAT_MS - 1));

    expect(first.count).toBe(1);
    expect(again).toMatchObject({ count: 2, at: first.at });
    expect(first.traceId).toMatch(/^[0-9a-f]{4}-[0-9a-f]{4}$/);
  });

  it("starts anew after 10 minutes", () => {
    captureError(uiError, at(0));
    const later = captureError(uiError, at(REPEAT_MS));

    expect(later).toMatchObject({ count: 1, at: at(REPEAT_MS).toISOString() });
  });

  it("does not count the same answer drawn again", () => {
    const first = captureProblem(problem(), "Не удалось опубликовать");
    const redrawn = captureProblem(problem(), "Не удалось опубликовать");
    const next = captureProblem(
      problem({ traceId: "1bf7651916cd43dd" }),
      "Не удалось опубликовать",
    );

    expect(redrawn).toBe(first);
    expect(next).toMatchObject({ count: 2, traceId: "1bf7651916cd43dd", source: "server" });
  });

  it("offers nothing for an error the user fixes himself", () => {
    expect(captureProblem(problem({ code: "rate_limited", reportable: false }), "Подождите")).toBe(
      undefined,
    );
  });

  it("takes an error without a trace for the interface's own, with its text", () => {
    const own = captureProblem(
      problem({ code: "internal", traceId: "", title: "Failed to fetch" }),
      "Что-то пошло не так",
    );

    expect(own).toMatchObject({ source: "interface", message: "Failed to fetch" });
  });

  it("keeps the text and the stack within the contract's bounds", () => {
    const long = captureError({ ...uiError, message: "a".repeat(2000), stack: "b".repeat(9000) });

    expect(long.message).toHaveLength(1000);
    expect(long.stack).toHaveLength(8000);
  });

  it("shows a long number by its start, a short one whole", () => {
    expect(shortNumber("0af7651916cd43dd8448eb211c80319c")).toBe("0af7-6519");
    expect(shortNumber("7f3a-c21e")).toBe("7f3a-c21e");
  });
});
