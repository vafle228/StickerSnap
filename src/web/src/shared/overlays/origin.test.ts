import { describe, expect, it } from "vitest";
import { originOf } from "./origin";

describe("popover origin", () => {
  it("grows from the corner that touches its button", () => {
    expect(originOf("bottom-end")).toBe("top right");
    expect(originOf("bottom-start")).toBe("top left");
    expect(originOf("top")).toBe("bottom center");
    expect(originOf("right-start")).toBe("top left");
    expect(originOf("left-end")).toBe("bottom right");
  });
});
