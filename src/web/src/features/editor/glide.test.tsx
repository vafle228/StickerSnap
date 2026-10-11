// @vitest-environment happy-dom
import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { glideNext, useGlide } from "./glide";

/* docs/ui-motion.md, principle 4: a jump by a click glides, a drag, a key or an undo does not. */

function frame(initial: string, held = false) {
  return renderHook(({ key, held: h }) => useGlide(key, h), {
    initialProps: { key: initial, held },
  });
}

describe("glide", () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it("glides a jump made right after a click, for a moment", () => {
    vi.useFakeTimers();
    const { result, rerender } = frame("square");
    glideNext();
    rerender({ key: "source", held: false });
    expect(result.current).toBe(true);

    act(() => vi.advanceTimersByTime(300));
    expect(result.current).toBe(false);
  });

  it("lands a change nobody clicked for at once: a key, an undo", () => {
    vi.useFakeTimers();
    const { result, rerender } = frame("square");
    glideNext();
    act(() => vi.advanceTimersByTime(500));
    rerender({ key: "source", held: false });
    expect(result.current).toBe(false);
  });

  it("stops gliding as soon as a drag begins", () => {
    const { result, rerender } = frame("square");
    glideNext();
    rerender({ key: "source", held: false });
    rerender({ key: "source", held: true });
    expect(result.current).toBe(false);
  });

  it("jumps at once when the system asks for less motion", () => {
    vi.stubGlobal("matchMedia", (query: string) => ({ matches: query.includes("reduce") }));
    const { result, rerender } = frame("square");
    glideNext();
    rerender({ key: "source", held: false });
    expect(result.current).toBe(false);
  });
});
