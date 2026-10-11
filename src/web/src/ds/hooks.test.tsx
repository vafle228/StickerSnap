// @vitest-environment happy-dom
import { act, cleanup, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { EXIT_FALLBACK_MS, usePresence } from "./hooks";

/* docs/ui-motion.md, section 5: a closing element stays until its exit has run, and gets out of
   the way of clicks, focus and screen readers at once. */

function Panel({ open, onExited }: { open: boolean; onExited?: () => void }) {
  const { mounted, state, appear, ref } = usePresence(open, onExited);
  if (!mounted) return null;
  return (
    <div ref={ref} data-testid="panel" data-state={state} data-appear={String(appear)}>
      <button type="button">Готово</button>
    </div>
  );
}

const panel = () => screen.queryByTestId("panel");

describe("usePresence", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => {
    cleanup();
    vi.useRealTimers();
  });

  it("keeps a closed element until the exit fallback, inert and hidden", () => {
    const onExited = vi.fn();
    const { rerender } = render(<Panel open onExited={onExited} />);
    rerender(<Panel open={false} onExited={onExited} />);

    const closing = panel();
    expect(closing?.dataset.state).toBe("closed");
    expect(closing?.inert).toBe(true);
    expect(closing?.getAttribute("aria-hidden")).toBe("true");
    expect(screen.queryByRole("button", { name: "Готово" })).toBeNull();

    act(() => vi.advanceTimersByTime(EXIT_FALLBACK_MS));
    expect(panel()).toBeNull();
    expect(onExited).toHaveBeenCalledOnce();
  });

  it("leaves as soon as its own transition ends, not a child's", () => {
    const { rerender } = render(<Panel open />);
    rerender(<Panel open={false} />);

    act(() => {
      screen.getByText("Готово").dispatchEvent(new Event("transitionend", { bubbles: true }));
    });
    expect(panel()).not.toBeNull();

    act(() => {
      panel()?.dispatchEvent(new Event("transitionend"));
    });
    expect(panel()).toBeNull();
  });

  it("opens again in place when reopened while leaving", () => {
    const onExited = vi.fn();
    const { rerender } = render(<Panel open onExited={onExited} />);
    rerender(<Panel open={false} onExited={onExited} />);
    rerender(<Panel open onExited={onExited} />);

    act(() => vi.advanceTimersByTime(EXIT_FALLBACK_MS));
    expect(panel()?.dataset.state).toBe("open");
    expect(panel()?.inert).toBe(false);
    expect(onExited).not.toHaveBeenCalled();
  });

  it("does not play the entry of what was on screen from the start", () => {
    const { rerender } = render(<Panel open />);
    expect(panel()?.dataset.appear).toBe("false");

    rerender(<Panel open={false} />);
    act(() => vi.advanceTimersByTime(EXIT_FALLBACK_MS));
    rerender(<Panel open />);
    expect(panel()?.dataset.appear).toBe("true");
  });
});
