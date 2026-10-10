import { describe, expect, it } from "vitest";
import { fps, kilobytes, num, seconds, speed, timecode } from "./format";

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

  it("writes ruler seconds without trailing zeros", () => {
    expect(seconds(12.4)).toBe("12,4");
    expect(seconds(10)).toBe("10");
    expect(seconds(0.5)).toBe("0,5");
  });

  it("counts kilobytes in 1024", () => {
    expect(kilobytes(262144)).toBe("256 КБ");
  });

  it("writes a time code to the millisecond", () => {
    expect(timecode(2.64)).toBe("00:02.640");
    expect(timecode(61.5)).toBe("01:01.500");
  });
});
