import { isValidElement } from "react";
import { beforeEach, describe, expect, it } from "vitest";
import { overlays, useOverlays } from "../overlays/overlays";
import { confirm } from "./notify";

/** Presses a button of the confirmation on screen, the way ConfirmDialog would. */
function press(button: "onConfirm" | "onCancel") {
  const slot = useOverlays.getState().confirm;
  if (!slot) throw new Error("no confirmation on screen");
  const element = slot.render(() => overlays.closeAll());
  if (!isValidElement<Record<typeof button, () => void>>(element))
    throw new Error("not an element");
  element.props[button]();
}

const ask = () => confirm({ title: "Удалить «Кот» с диска?", confirmLabel: "Удалить" });

describe("confirm", () => {
  beforeEach(() => overlays.closeAll());

  it("says yes on the verb and closes", async () => {
    const answer = ask();
    press("onConfirm");

    await expect(answer).resolves.toBe(true);
    expect(useOverlays.getState().confirm).toBeNull();
  });

  it("says no on «Отмена»", async () => {
    const answer = ask();
    press("onCancel");
    await expect(answer).resolves.toBe(false);
  });

  it("says no when another confirmation takes its place", async () => {
    const first = ask();
    const second = ask();

    await expect(first).resolves.toBe(false);
    press("onConfirm");
    await expect(second).resolves.toBe(true);
  });

  it("says no when the section changes and everything closes", async () => {
    const answer = ask();
    overlays.closeAll();
    await expect(answer).resolves.toBe(false);
  });
});
