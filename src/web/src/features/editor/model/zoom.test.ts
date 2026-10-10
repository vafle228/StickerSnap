import { describe, expect, it } from "vitest";
import { fitScale, zoomIn, zoomOut } from "./zoom";

describe("zoom", () => {
  it("fits the source into the stage", () => {
    expect(fitScale({ width: 1920, height: 1080 }, { width: 960, height: 960 })).toBe(0.5);
  });

  it("steps from «По окну» to the neighbouring steps", () => {
    expect(zoomIn(0.6)).toBe(0.75);
    expect(zoomOut(0.6)).toBe(0.5);
    expect(zoomIn(1)).toBe(1.5);
  });

  it("stops at the ends", () => {
    expect(zoomIn(4)).toBe(4);
    expect(zoomOut(0.1)).toBe(0.1);
  });
});
