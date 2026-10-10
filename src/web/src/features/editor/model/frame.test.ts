import { describe, expect, it } from "vitest";
import {
  defaultFrame,
  fitSpeed,
  frameInput,
  outputSize,
  segmentDuration,
  sourceCrop,
  squareCrop,
  squashStretch,
} from "./frame";

const fullHd = { width: 1920, height: 1080 };
const sticker = { side: 512 };

describe("frame presets", () => {
  it("«Квадрат» takes the biggest centered square", () => {
    expect(squareCrop(fullHd).rect).toEqual({ x: 420, y: 0, width: 1080, height: 1080 });
  });

  it("«Как в источнике» takes the whole frame", () => {
    expect(sourceCrop(fullHd).rect).toEqual({ x: 0, y: 0, width: 1920, height: 1080 });
  });

  it("starts a crop square and a stretch as the source", () => {
    expect(defaultFrame("crop", fullHd)).toMatchObject({ mode: "crop", preset: "square" });
    expect(defaultFrame("stretch", fullHd)).toEqual({
      mode: "stretch",
      preset: "source",
      aspect: { width: 1920, height: 1080 },
    });
  });
});

describe("outputSize", () => {
  it("gives a square crop 512 × 512 (artboard 3a)", () => {
    expect(outputSize(squareCrop(fullHd), "sticker", sticker)).toEqual({
      width: 512,
      height: 512,
    });
  });

  it("keeps the longer side at 512 for a stretch: 1920 × 1080 → 512 × 288", () => {
    expect(outputSize(defaultFrame("stretch", fullHd), "sticker", sticker)).toEqual({
      width: 512,
      height: 288,
    });
  });

  it("squashes to 1:1", () => {
    expect(outputSize(squashStretch(), "sticker", sticker)).toEqual({ width: 512, height: 512 });
  });

  it("makes an emoji a full square whatever the frame", () => {
    expect(outputSize(sourceCrop(fullHd), "emoji", { side: 100 })).toEqual({
      width: 100,
      height: 100,
    });
  });

  it("reads the input of a crop from the rect and of a stretch from the source", () => {
    expect(frameInput(squareCrop(fullHd), fullHd)).toEqual({ width: 1080, height: 1080 });
    expect(frameInput(squashStretch(), fullHd)).toEqual(fullHd);
  });
});

describe("timing", () => {
  const segment = { inFrame: 0, outFrame: 89, excludedFrames: [], speed: 1 };

  it("counts In and Out inclusive", () => {
    expect(segmentDuration(segment, 30)).toBe(3);
    expect(segmentDuration({ ...segment, speed: 2 }, 30)).toBe(1.5);
  });

  it("«Вписать целиком» picks the slowest speed that fits, rounded up", () => {
    // 4,84 s at 30 fps into 3 s: 1,6133… → 1,62×
    expect(fitSpeed({ ...segment, outFrame: 144 }, 30, 3)).toBe(1.62);
    expect(segmentDuration({ ...segment, outFrame: 144, speed: 1.62 }, 30)).toBeLessThanOrEqual(3);
  });

  it("keeps «Вписать целиком» between 0,25× and 4×", () => {
    expect(fitSpeed({ ...segment, outFrame: 1 }, 30, 3)).toBe(0.25);
    expect(fitSpeed({ ...segment, outFrame: 999 }, 30, 3)).toBe(4);
  });
});
