// @vitest-environment happy-dom
import { afterEach, describe, expect, it } from "vitest";
import { createFakeTelegram } from "./fakeTelegram";
import { applyTheme } from "./theme";

afterEach(() => {
  document.documentElement.removeAttribute("data-theme");
  document.documentElement.style.removeProperty("--bg");
});

describe("the Mini App theme", () => {
  it("takes Telegram's light or dark, not its colours", () => {
    const telegram = createFakeTelegram({ colorScheme: "dark" });
    applyTheme(telegram);
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
  });

  it("paints Telegram's header and background in our background colour", () => {
    const telegram = createFakeTelegram();
    document.documentElement.style.setProperty("--bg", "#f4f1ec");
    applyTheme(telegram);
    expect(telegram.calls.painted).toEqual(["#f4f1ec"]);
  });
});
