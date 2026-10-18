import { describe, expect, it } from "vitest";
import { requestedLabel, systemLabel } from "./format";

describe("the link request", () => {
  it("names the system the way the desktop does, else by its family", () => {
    expect(systemLabel({ os: "windows", platform: "Windows 11" })).toBe("Windows 11");
    expect(systemLabel({ os: "macos" })).toBe("macOS");
    expect(systemLabel({ os: "linux", platform: " " })).toBe("Linux");
  });

  it("says how long ago «Привязать» was pressed", () => {
    const now = Date.parse("2026-10-18T12:10:00Z");
    expect(requestedLabel("2026-10-18T12:09:30Z", now)).toBe("только что");
    expect(requestedLabel("2026-10-18T12:09:00Z", now)).toBe("1 минуту назад");
    expect(requestedLabel("2026-10-18T12:08:00Z", now)).toBe("2 минуты назад");
    expect(requestedLabel("2026-10-18T12:01:00Z", now)).toBe("9 минут назад");
  });
});
