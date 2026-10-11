import { describe, expect, it } from "vitest";
import type { Segment } from "../shared/api/types";
import { app, createDb } from "./data";
import { bytesPerFrame, climb, estimateOf, ladder } from "./encoder";

const project = createDb("ok").projects[0];
if (!project) throw new Error("demo data has no project");
const segment = project.scene.segments[0] as Segment;
const limits = app.limits.sticker;

describe("demo encoder (forecast and ladder agree)", () => {
  it("lets a quiet 3 s sticker fit as it is", () => {
    const estimate = estimateOf(project.source, segment, limits, bytesPerFrame("ok"));
    expect(estimate.fits).toBe(true);
    expect(estimate.likelyConcessions).toEqual([]);
    expect(estimate.suggestions).toEqual([]);
  });

  it("forecasts a busy one over the limit and offers a shorter sticker (3c, G)", () => {
    const estimate = estimateOf(project.source, segment, limits, bytesPerFrame("too-big"));
    expect(estimate.fits).toBe(false);
    expect(estimate.bytes).toBeGreaterThan(limits.maxBytes);
    expect(estimate.likelyConcessions).toEqual([{ kind: "fps", from: 30, to: 20 }]);
    expect(estimate.suggestions.map((s) => s.kind)).toEqual(["trim", "speed"]);
  });

  it("climbs fps first, then CRF, then the side, and stops at the first that fits", () => {
    const rungs = ladder(project.source, segment, limits, bytesPerFrame("too-big"));
    expect(rungs.map((r) => [r.fps, r.crf, r.side])).toEqual([
      [30, 31, 512],
      [24, 31, 512],
      [20, 31, 512],
      [20, 37, 512],
      [20, 37, 448],
      [20, 37, 384],
    ]);
    const { tried, fits } = climb(rungs, limits.maxBytes);
    expect(fits).toBe(true);
    expect(tried.at(-1)?.bytes).toBeLessThanOrEqual(limits.maxBytes);
  });
});
