import { describe, expect, it } from "vitest";
import { segmentDuration } from "./frame";
import {
  fitWhole,
  frameAt,
  maxFrames,
  moveEdge,
  newRange,
  newSegment,
  nextNumber,
  orderedSelection,
  pxPerFrame,
  slide,
  stepTime,
  tickStep,
  zoomFor,
} from "./timeline";

const cut = (inFrame: number, outFrame: number, excludedFrames: number[] = []) => ({
  inFrame,
  outFrame,
  excludedFrames,
});
// 8 s at 30 fps, 3,00 s at 1× is 90 frames
const bounds = { total: 240, max: 90 };

describe("frames and time", () => {
  it("puts 2,64 s at 30 fps on frame 79, as artboard 3a", () => {
    expect(frameAt(2.64, 30)).toBe(79);
    expect(frameAt(79 / 30, 30)).toBe(79);
  });

  it("steps one frame and stays inside the source", () => {
    expect(stepTime(79 / 30, 1, 30, 240)).toBeCloseTo(80 / 30);
    expect(stepTime(0, -1, 30, 240)).toBe(0);
    expect(stepTime(239 / 30, 1, 30, 240)).toBeCloseTo(239 / 30);
  });

  it("steps whole frames at 29,97 fps", () => {
    const fps = 30000 / 1001;
    let time = 0;
    for (let i = 0; i < 1000; i++) time = stepTime(time, 1, fps, 5000);
    expect(frameAt(time, fps)).toBe(1000);
  });
});

describe("the 3,00 s stop", () => {
  it("counts the frames the limit allows at a speed", () => {
    expect(maxFrames(3, 30, 1)).toBe(90);
    expect(maxFrames(3, 30, 1.25)).toBe(112);
    expect(maxFrames(3, 30, 0.25)).toBe(22);
  });

  it("stops O where the segment reaches the limit", () => {
    expect(moveEdge(cut(10, 50), "out", 200, bounds)).toEqual({
      inFrame: 10,
      outFrame: 99,
      limited: true,
    });
    expect(moveEdge(cut(10, 50), "out", 99, bounds).limited).toBe(false);
  });

  it("stops I the same way", () => {
    expect(moveEdge(cut(100, 150), "in", 0, bounds)).toEqual({
      inFrame: 61,
      outFrame: 150,
      limited: true,
    });
  });

  it("gives the excluded frames back as room", () => {
    expect(moveEdge(cut(10, 50, [20, 21]), "out", 200, bounds).outFrame).toBe(101);
  });

  it("keeps a dragged edge from crossing the other one", () => {
    expect(moveEdge(cut(10, 50), "in", 80, bounds)).toEqual({
      inFrame: 50,
      outFrame: 50,
      limited: false,
    });
    expect(moveEdge(cut(10, 50), "out", 0, bounds).outFrame).toBe(10);
  });

  it("lets I and O pressed past the other edge take it along", () => {
    expect(moveEdge(cut(10, 50), "in", 120, bounds, true)).toEqual({
      inFrame: 120,
      outFrame: 120,
      limited: false,
    });
    expect(moveEdge(cut(100, 150), "out", 40, bounds, true)).toMatchObject({
      inFrame: 40,
      outFrame: 40,
    });
  });

  it("does not call the end of the video a limit", () => {
    expect(moveEdge(cut(200, 220), "out", 400, bounds)).toEqual({
      inFrame: 200,
      outFrame: 239,
      limited: false,
    });
  });

  it("lets a segment over the limit shrink but not grow", () => {
    // 120 frames: 4,00 s at 1×, the speed was lowered after the trim
    const long = cut(0, 119);
    expect(moveEdge(long, "out", 130, bounds)).toMatchObject({ outFrame: 119, limited: true });
    expect(moveEdge(long, "out", 100, bounds)).toMatchObject({ outFrame: 100, limited: false });
  });

  it("moves freely while the limits are unknown", () => {
    expect(moveEdge(cut(10, 50), "out", 200, { total: 240, max: Infinity }).outFrame).toBe(200);
  });
});

describe("dragging the whole segment", () => {
  it("keeps the length and stops at the ends of the video", () => {
    expect(slide(cut(10, 50), 30, 240)).toEqual({ inFrame: 40, outFrame: 80 });
    expect(slide(cut(10, 50), -30, 240)).toEqual({ inFrame: 0, outFrame: 40 });
    expect(slide(cut(10, 50), 500, 240)).toEqual({ inFrame: 199, outFrame: 239 });
  });
});

describe("«Вписать целиком» from the stop", () => {
  it("takes the pushed frame and speeds the segment up to fit", () => {
    const fit = fitWhole(cut(0, 89), "out", 119, 30, 3, 240);
    expect(fit).toEqual({ inFrame: 0, outFrame: 119, speed: 1.34 });
    expect(segmentDuration({ ...fit, excludedFrames: [] }, 30)).toBeLessThanOrEqual(3);
  });

  it("stops where even 4× cannot fit", () => {
    expect(fitWhole(cut(0, 89), "out", 1000, 30, 3, 2000)).toEqual({
      inFrame: 0,
      outFrame: 359,
      speed: 4,
    });
  });
});

describe("new segments", () => {
  it("makes one from a selection", () => {
    expect(newRange(5, orderedSelection(84, 80), bounds)).toEqual({ inFrame: 80, outFrame: 84 });
    expect(newRange(5, orderedSelection(0, 200), bounds)).toEqual({ inFrame: 0, outFrame: 89 });
  });

  it("starts at the playhead and runs to the limit or the end", () => {
    expect(newRange(100, undefined, bounds)).toEqual({ inFrame: 100, outFrame: 189 });
    expect(newRange(200, undefined, bounds)).toEqual({ inFrame: 200, outFrame: 239 });
    expect(newRange(100, { from: 7, to: 7 }, bounds).inFrame).toBe(100);
  });

  it("numbers the next sticker after the biggest one", () => {
    expect(nextNumber(["Стикер 1", "Стикер 4", "Кот"])).toBe(5);
    expect(nextNumber([])).toBe(1);
  });

  it("copies the frame, the preset and the loop, not the speed", () => {
    const base = {
      id: "seg-1",
      name: "Стикер 1",
      inFrame: 0,
      outFrame: 89,
      excludedFrames: [],
      frame: {
        mode: "stretch" as const,
        preset: "squash" as const,
        aspect: { width: 1, height: 1 },
      },
      speed: 2,
      loop: "pingpong" as const,
      preset: "emoji" as const,
      longSticker: false,
    };
    const segment = newSegment(base, { inFrame: 10, outFrame: 20 }, "seg-2", "Стикер 2");
    expect(segment).toMatchObject({ speed: 1, loop: "pingpong", preset: "emoji", inFrame: 10 });
    expect(segment.frame).toEqual(base.frame);
    expect(segment.frame).not.toBe(base.frame);
  });
});

describe("the scale", () => {
  it("fits the whole video at «Обзор» and reaches whole frames at «Кадры»", () => {
    expect(pxPerFrame(0, 960, 240)).toBe(4);
    expect(pxPerFrame(1, 960, 240)).toBeCloseTo(48);
  });

  it("keeps a short GIF in frames at any zoom", () => {
    expect(pxPerFrame(0, 960, 18)).toBeCloseTo(960 / 18);
    expect(pxPerFrame(1, 960, 18)).toBeCloseTo(960 / 18);
    expect(zoomFor(30, 960, 18)).toBe(0);
  });

  it("finds the zoom for a cell width", () => {
    expect(pxPerFrame(zoomFor(24, 960, 240), 960, 240)).toBeCloseTo(24);
  });

  it("spaces the ruler labels", () => {
    expect(tickStep(80)).toBe(1);
    expect(tickStep(20)).toBe(5);
    expect(tickStep(1000)).toBe(0.1);
  });
});
