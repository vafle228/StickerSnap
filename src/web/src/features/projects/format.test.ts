import { describe, expect, it } from "vitest";
import type { ProjectSummary } from "../../shared/api/types";
import { durationChip, projectMeta, segmentCount, when } from "./format";

const now = new Date(2026, 9, 10, 18, 30);
const at = (y: number, m: number, d: number, h = 12, min = 0) =>
  new Date(y, m, d, h, min).toISOString();

describe("when", () => {
  it("counts minutes in the first hour", () => {
    expect(when(at(2026, 9, 10, 18, 30), now)).toBe("только что");
    expect(when(at(2026, 9, 10, 18, 29), now)).toBe("1 минуту назад");
    expect(when(at(2026, 9, 10, 18, 27), now)).toBe("3 минуты назад");
    expect(when(at(2026, 9, 10, 18, 18), now)).toBe("12 минут назад");
    expect(when(at(2026, 9, 10, 17, 49), now)).toBe("41 минуту назад");
  });

  it("names today, yesterday and older days", () => {
    expect(when(at(2026, 9, 10, 14, 5), now)).toBe("сегодня, 14:05");
    expect(when(at(2026, 9, 9, 23, 50), now)).toBe("вчера");
    expect(when(at(2026, 9, 3), now)).toBe("3 октября");
    expect(when(at(2025, 11, 30), now)).toBe("30 декабря 2025");
  });
});

const summary = (patch: Partial<ProjectSummary>): ProjectSummary => ({
  id: "p",
  name: "Кот",
  updatedAt: at(2026, 9, 9),
  segmentCount: 3,
  preset: "sticker",
  durationSec: 2.83,
  coverUrl: "/c",
  sourceStatus: "ok",
  ...patch,
});

describe("project meta", () => {
  it("counts segments by preset", () => {
    expect(segmentCount({ segmentCount: 1, preset: "sticker" })).toBe("1 стикер");
    expect(segmentCount({ segmentCount: 3, preset: "sticker" })).toBe("3 стикера");
    expect(segmentCount({ segmentCount: 8, preset: "sticker" })).toBe("8 стикеров");
    expect(segmentCount({ segmentCount: 5, preset: "emoji" })).toBe("5 эмодзи");
    expect(segmentCount({ segmentCount: 4, preset: "mixed" })).toBe("4 отрезка");
    expect(segmentCount({ segmentCount: 0, preset: "sticker" })).toBe("Нет отрезков");
  });

  it("puts the count and the date together, or says the source is gone", () => {
    expect(projectMeta(summary({}), now)).toBe("3 стикера · вчера");
    expect(projectMeta(summary({ sourceStatus: "missing" }), now)).toBe("Исходник не найден");
  });

  it("shows the longest segment on the cover", () => {
    expect(durationChip(summary({}))).toBe("2,8 с");
    expect(durationChip(summary({ segmentCount: 0 }))).toBeUndefined();
  });
});
