import { describe, expect, it } from "vitest";
import { fps, kilobytes, num, speed, timecode } from "./format";

describe("format", () => {
  it("writes numbers with a decimal comma", () => {
    expect(num(2.84, 2)).toBe("2,84");
    expect(fps(29.97)).toBe("29,97");
    expect(fps(30)).toBe("30");
  });

  it("drops trailing zeros of a speed", () => {
    expect(speed(1.25)).toBe("1,25×");
    expect(speed(1.5)).toBe("1,5×");
    expect(speed(4)).toBe("4×");
  });

  it("counts kilobytes in 1024", () => {
    expect(kilobytes(262144)).toBe("256 КБ");
  });

  it("writes a time code to the millisecond", () => {
    expect(timecode(2.64)).toBe("00:02.640");
    expect(timecode(61.5)).toBe("01:01.500");
  });
});
