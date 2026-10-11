// @vitest-environment happy-dom
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import "../../test/dom";
import { Dialog } from "../../ds";
import { hasModalLayer } from "../../ds/layers";
import { confirm } from "../notify";
import { OverlayHost } from "./OverlayHost";
import { overlays } from "./overlays";

/* Artboard «12 · Окна и попапы»: a window, a confirmation above it, one Esc per layer. */

function openWindow() {
  act(() => {
    overlays.openWindow((close) => <Dialog title="Настройки пака" onClose={close} />);
  });
}

describe("windows and confirmations", () => {
  it("closes only the confirmation on Esc, and the window on the next Esc", async () => {
    const user = userEvent.setup();
    render(<OverlayHost />);
    openWindow();
    let answer: Promise<boolean> | undefined;
    act(() => {
      answer = confirm({
        title: "Удалить пак?",
        text: "Его нельзя вернуть.",
        confirmLabel: "Удалить",
      });
    });
    expect(screen.getByRole("alertdialog")).toBeTruthy();

    await user.keyboard("{Escape}");
    expect(await answer).toBe(false);
    expect(screen.queryByRole("alertdialog")).toBeNull();
    expect(screen.getByRole("dialog", { name: "Настройки пака" })).toBeTruthy();

    await user.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("keeps editor hotkeys silent while a window is open", async () => {
    const user = userEvent.setup();
    render(<OverlayHost />);
    expect(hasModalLayer()).toBe(false);

    openWindow();
    expect(hasModalLayer()).toBe(true);

    await user.keyboard("{Escape}");
    expect(hasModalLayer()).toBe(false);
  });

  it("lets a closed window fade out without holding the focus or the next Esc", async () => {
    const user = userEvent.setup();
    render(<OverlayHost />);
    openWindow();

    await user.keyboard("{Escape}");
    const leaving = document.querySelector<HTMLElement>(".ss-overlay");
    expect(leaving?.dataset.state).toBe("closed");
    expect(leaving?.inert).toBe(true);
    expect(hasModalLayer()).toBe(false);

    openWindow();
    expect(screen.getByRole("dialog", { name: "Настройки пака" })).toBeTruthy();
    await waitFor(() => expect(document.querySelectorAll(".ss-overlay")).toHaveLength(1));
  });
});
