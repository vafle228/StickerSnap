import { describe, expect, it } from "vitest";
import { plural, t } from "./ru";

describe("plural", () => {
  it("picks the Russian form of a count", () => {
    expect([1, 21, 101].map(plural)).toEqual(["one", "one", "one"]);
    expect([2, 3, 4, 22, 104].map(plural)).toEqual(["few", "few", "few", "few", "few"]);
    expect([0, 5, 11, 12, 14, 111, 25].map(plural)).toEqual(Array(7).fill("many"));
  });

  it("reads as a phrase", () => {
    expect(t(`editor.selection.${plural(3)}`, { n: 3 })).toBe("Выделено 3 кадра");
    expect(t(`editor.selection.${plural(21)}`, { n: 21 })).toBe("Выделен 21 кадр");
  });
});
