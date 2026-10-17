import { describe, expect, it } from "vitest";
import { deviceLabel, devicesLabel } from "./format";

describe("device words", () => {
  it("name the system and the day it was linked", () => {
    expect(deviceLabel({ os: "windows", linkedAt: "2026-10-12T08:30:00Z" })).toBe(
      "Windows · с 12 октября",
    );
    expect(deviceLabel({ os: "linux", linkedAt: "2026-10-03T17:10:00Z" })).toBe(
      "Linux · с 3 октября",
    );
  });

  it("count linked computers", () => {
    expect(devicesLabel(1)).toBe("1 компьютер привязан");
    expect(devicesLabel(2)).toBe("2 компьютера привязано");
    expect(devicesLabel(5)).toBe("5 компьютеров привязано");
    expect(devicesLabel(0)).toBe("Компьютеров пока нет");
  });
});
