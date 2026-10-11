import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { background, BACKGROUND, initialSize } from "./look.js";

interface Tokens {
  color: { tokens: { name: string; value: { light: string; dark: string } }[] };
}

describe("the window before the SPA paints", () => {
  it("has the colour of token bg in both themes", () => {
    const url = new URL("../../web/src/ds/tokens.json", import.meta.url);
    const tokens = JSON.parse(readFileSync(url, "utf8")) as Tokens;
    const bg = tokens.color.tokens.find((t) => t.name === "bg");
    expect(bg?.value).toEqual(BACKGROUND);
  });

  it("follows the saved theme, and the system's when it is «Как в системе»", () => {
    expect(background("dark", false)).toBe(BACKGROUND.dark);
    expect(background("light", true)).toBe(BACKGROUND.light);
    expect(background("system", true)).toBe(BACKGROUND.dark);
    expect(background("system", false)).toBe(BACKGROUND.light);
  });

  it("opens at the artboard size, smaller on a small screen, never below the minimum", () => {
    expect(initialSize({ width: 2560, height: 1400 })).toEqual({ width: 1440, height: 900 });
    expect(initialSize({ width: 1366, height: 728 })).toEqual({ width: 1366, height: 728 });
    expect(initialSize({ width: 800, height: 600 })).toEqual({ width: 1024, height: 700 });
  });
});
