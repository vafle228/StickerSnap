import { beforeEach, describe, expect, it, vi } from "vitest";
import { overlays, useOverlays } from "./overlays";

describe("overlays", () => {
  beforeEach(() => overlays.closeAll());

  it("keeps one window: a new one replaces the open one and closes it as «Отмена»", () => {
    const first = vi.fn();
    const closeFirst = overlays.openWindow(() => "first", first);
    overlays.openWindow(() => "second");

    expect(first).toHaveBeenCalledOnce();
    expect(useOverlays.getState().window?.render(() => {})).toBe("second");

    // A stale close of the replaced window leaves the new one alone.
    closeFirst();
    expect(useOverlays.getState().window).not.toBeNull();
  });

  it("holds a confirmation above the window without touching it", () => {
    overlays.openWindow(() => "window");
    const close = overlays.openConfirm(() => "confirm");
    close();

    expect(useOverlays.getState().confirm).toBeNull();
    expect(useOverlays.getState().window).not.toBeNull();
  });
});
