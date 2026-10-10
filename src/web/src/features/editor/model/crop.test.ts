import { describe, expect, it } from "vitest";
import {
  cropPreset,
  moveCrop,
  presetAspect,
  resizeCrop,
  resizeStretch,
  stretchBox,
  stretchOf,
} from "./crop";

const fullHd = { width: 1920, height: 1080 };
const square = { x: 420, y: 0, width: 1080, height: 1080 };

describe("moving the crop", () => {
  it("moves the rect by whole pixels", () => {
    expect(moveCrop(square, -100.4, 0, fullHd)).toEqual({ ...square, x: 320 });
  });

  it("stops at the edges of the source", () => {
    expect(moveCrop(square, 2000, 50, fullHd)).toEqual({ ...square, x: 840, y: 0 });
    expect(moveCrop(square, -2000, -50, fullHd)).toEqual({ ...square, x: 0, y: 0 });
  });
});

describe("resizing the crop", () => {
  it("keeps the opposite corner where it was", () => {
    const rect = resizeCrop(square, "se", -200, -200, fullHd, 1, 16);
    expect(rect).toEqual({ x: 420, y: 0, width: 880, height: 880 });
    const fromNw = resizeCrop(square, "nw", 300, 300, fullHd, 1, 16);
    expect(fromNw).toEqual({ x: 720, y: 300, width: 780, height: 780 });
  });

  it("holds the square and follows the axis the pointer went further along", () => {
    const rect = resizeCrop(square, "se", -500, -100, fullHd, 1, 16);
    expect(rect).toEqual({ x: 420, y: 0, width: 580, height: 580 });
    const down = resizeCrop(square, "se", -50, -300, fullHd, 1, 16);
    expect(down).toEqual({ x: 420, y: 0, width: 780, height: 780 });
  });

  it("holds the source proportion for «Как в источнике»", () => {
    const whole = { x: 0, y: 0, ...fullHd };
    const rect = resizeCrop(whole, "se", -960, 0, fullHd, presetAspect("source", fullHd), 16);
    expect(rect).toEqual({ x: 0, y: 0, width: 960, height: 540 });
  });

  it("changes the sides freely without an aspect", () => {
    const rect = resizeCrop(square, "ne", 200, 300, fullHd, undefined, 16);
    expect(rect).toEqual({ x: 420, y: 300, width: 1280, height: 780 });
  });

  it("never leaves the source", () => {
    const rect = resizeCrop(square, "se", 900, 900, fullHd, 1, 16);
    expect(rect).toEqual(square);
    const free = resizeCrop(square, "nw", -900, -900, fullHd, undefined, 16);
    expect(free).toEqual({ x: 0, y: 0, width: 1500, height: 1080 });
  });

  it("stops at the minimum instead of turning inside out", () => {
    const rect = resizeCrop(square, "se", -3000, -3000, fullHd, 1, 64);
    expect(rect).toEqual({ x: 420, y: 0, width: 64, height: 64 });
  });
});

describe("crop presets", () => {
  it("names the proportion a free resize ended on", () => {
    expect(cropPreset({ width: 300, height: 300 }, fullHd)).toBe("square");
    expect(cropPreset({ width: 640, height: 360 }, fullHd)).toBe("source");
    expect(cropPreset({ width: 641, height: 360 }, fullHd)).toBe("source");
    expect(cropPreset({ width: 700, height: 360 }, fullHd)).toBe("free");
  });
});

describe("the stretch box", () => {
  it("fills the source for its own proportion and centers a square", () => {
    expect(stretchBox(fullHd, fullHd)).toEqual({ x: 0, y: 0, ...fullHd });
    expect(stretchBox({ width: 1, height: 1 }, fullHd)).toEqual({
      x: 420,
      y: 0,
      width: 1080,
      height: 1080,
    });
  });

  it("grows a dragged side both ways from the center, up to the source", () => {
    const box = stretchBox({ width: 1, height: 1 }, fullHd);
    expect(resizeStretch(box, "e", 100, fullHd, 16)).toMatchObject({ x: 320, width: 1280 });
    expect(resizeStretch(box, "w", 1000, fullHd, 16)).toMatchObject({ x: 0, width: 1920 });
    expect(resizeStretch(box, "n", -270, fullHd, 16)).toMatchObject({ y: 270, height: 540 });
  });

  it("turns a box into a stretch and spots the presets", () => {
    expect(stretchOf({ width: 1280, height: 1080 }, fullHd)).toEqual({
      mode: "stretch",
      preset: "free",
      aspect: { width: 1280, height: 1080 },
    });
    expect(stretchOf({ width: 1080, height: 1080 }, fullHd).preset).toBe("squash");
    expect(stretchOf({ width: 1920, height: 1080 }, fullHd).preset).toBe("source");
  });
});
