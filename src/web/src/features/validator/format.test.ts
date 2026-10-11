import { describe, expect, it } from "vitest";
import { checkValue } from "./format";

describe("check values in plain Russian (R8)", () => {
  it("turns the server's bounds into words", () => {
    expect(checkValue("size", "≤ 262144")).toBe("до 256 КБ");
    expect(checkValue("size", "277504")).toBe("271 КБ");
    expect(checkValue("duration", "≤ 3")).toBe("до 3,00 с");
    expect(checkValue("duration", "2.97")).toBe("2,97 с");
    expect(checkValue("fps", "≤ 30")).toBe("до 30 fps");
    expect(checkValue("dimensions", "512")).toBe("сторона 512");
    expect(checkValue("dimensions", "640×360")).toBe("640 × 360");
    expect(checkValue("codec", "vp9")).toBe("VP9");
    expect(checkValue("audio", "none")).toBe("нет");
    expect(checkValue("audio", "opus")).toBe("opus");
    expect(checkValue("audio", "none", "expected")).toBe("без звука");
    expect(checkValue("alpha", "any")).toBe("любая");
    expect(checkValue("alpha", "yuva420p")).toBe("есть");
  });
});
