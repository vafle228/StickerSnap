import { describe, expect, it } from "vitest";
import { plural } from "./plural";

const STICKERS = ["стикер", "стикера", "стикеров"] as const;

describe("plural", () => {
  it.each([
    [1, "стикер"],
    [2, "стикера"],
    [5, "стикеров"],
    [11, "стикеров"],
    [12, "стикеров"],
    [21, "стикер"],
    [22, "стикера"],
    [111, "стикеров"],
    [120, "стикеров"],
    [0, "стикеров"],
  ])("%i %s", (n, word) => {
    expect(plural(n, STICKERS)).toBe(word);
  });
});
