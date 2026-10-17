// @vitest-environment happy-dom
import { launch } from "../../test/dom";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { useNav } from "../../app/nav";

function openDevices(options: Parameters<typeof launch>[0] = {}) {
  const launched = launch(options);
  useNav.getState().push({ kind: "devices" });
  return launched;
}

describe("«Устройства»", () => {
  it("lists the linked computers with their system and the day they were linked", async () => {
    openDevices();
    const list = await screen.findByRole("list", { name: "Компьютеры" });
    expect(within(list).getByText("DESKTOP-7F3K2Q")).toBeTruthy();
    expect(within(list).getByText("Windows · с 12 октября")).toBeTruthy();
    expect(within(list).getByText("Linux · с 3 октября")).toBeTruthy();
  });

  it("has no «Привязать»: a computer is linked only on itself", async () => {
    openDevices();
    await screen.findByRole("list", { name: "Компьютеры" });
    expect(screen.queryByRole("button", { name: /Привязать/ })).toBeNull();
  });

  it("unlinks a computer after asking", async () => {
    const { telegram } = openDevices();
    await userEvent.click(await screen.findByRole("button", { name: "Отвязать DESKTOP-7F3K2Q" }));
    const confirm = screen.getByRole("alertdialog", { name: "Отвязать DESKTOP-7F3K2Q?" });
    await userEvent.click(within(confirm).getByRole("button", { name: "Отвязать" }));
    await waitFor(() => expect(screen.queryByText("DESKTOP-7F3K2Q")).toBeNull());
    expect(screen.getByText("ubuntu-laptop")).toBeTruthy();
    expect(telegram.calls.haptics).toEqual(["confirm", "done"]);
  });

  it("explains where to link one when there are none", async () => {
    openDevices({ scenario: "no-devices" });
    expect(await screen.findByRole("heading", { name: "Компьютеров пока нет" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Скачать StickerSnap" })).toBeTruthy();
  });
});
