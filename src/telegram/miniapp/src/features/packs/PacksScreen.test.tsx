// @vitest-environment happy-dom
import { launch } from "../../test/dom";
import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { useNav } from "../../app/nav";

describe("«Мои паки»", () => {
  it("lists the packs fresh first, with their count and «Заполнен» on a full one", async () => {
    launch();
    const list = await screen.findByRole("list", { name: "Паки" });
    const rows = within(list).getAllByRole("button");
    expect(rows.map((r) => r.textContent)).toEqual([
      "Реакции с созвонаСтикеры · 12 из 120",
      "Кот и диванСтикеры · 5 из 120",
      "Эмодзи для чатаЭмодзи · 18 из 200",
      "Лето 2026Стикеры · 4 из 120",
      "Сборник 2025Стикеры · 120 из 120Заполнен",
    ]);
  });

  it("counts the linked computers on the «Устройства» card and opens them", async () => {
    launch();
    const card = await screen.findByRole("button", { name: /Устройства/ });
    expect(await within(card).findByText("2 компьютера привязано")).toBeTruthy();
    await userEvent.click(card);
    expect(await screen.findByRole("heading", { name: "Устройства" })).toBeTruthy();
  });

  it("opens a pack on a tap, with «Назад» in Telegram's header", async () => {
    const { telegram } = launch();
    expect(telegram.calls.backButton).toBe(false);
    await userEvent.click(await screen.findByRole("button", { name: /Кот и диван/ }));
    expect(await screen.findByRole("heading", { name: "Кот и диван" })).toBeTruthy();
    expect(telegram.calls.backButton).toBe(true);
    telegram.pressBack();
    expect(await screen.findByRole("heading", { name: "Мои паки" })).toBeTruthy();
    expect(useNav.getState().stack).toHaveLength(1);
  });

  it("has no «Создать пак»: without packs it sends to the desktop app", async () => {
    const { telegram } = launch({ scenario: "no-packs" });
    expect(await screen.findByRole("heading", { name: "Паков пока нет" })).toBeTruthy();
    await userEvent.click(screen.getByRole("button", { name: "Скачать StickerSnap" }));
    expect(telegram.calls.links).toEqual([
      "https://github.com/vafle228/StickerSnap/releases/latest",
    ]);
  });

  it("says when the service cannot be reached, and asks again on «Повторить»", async () => {
    const { db } = launch({ scenario: "offline" });
    expect(await screen.findByText("Не удалось загрузить паки")).toBeTruthy();
    db.scenario = "ok";
    await userEvent.click(screen.getByRole("button", { name: "Повторить" }));
    expect(await screen.findByRole("list", { name: "Паки" })).toBeTruthy();
  });

  it("marks a pack Telegram no longer knows as «Не найден»", async () => {
    launch({ scenario: "pack-missing" });
    const row = await screen.findByRole("button", { name: /Первый тест/ });
    expect(within(row).getByText("Не найден")).toBeTruthy();
  });
});

describe("signing in", () => {
  it("shows «Не получилось войти» when Telegram's signature does not pass", async () => {
    const { telegram } = launch({ scenario: "unauthorized" });
    expect(await screen.findByText("Не получилось войти")).toBeTruthy();
    await userEvent.click(screen.getByRole("button", { name: "Закрыть" }));
    expect(telegram.calls.closed).toBe(true);
  });

  it("shows it at once outside Telegram, where there is nothing to sign with", () => {
    launch({ initData: "" });
    expect(screen.getByText("Не получилось войти")).toBeTruthy();
  });
});

describe("Telegram's frame", () => {
  it("is told the Mini App is ready and gets «Настройки» in its «⋯» menu", async () => {
    const { telegram } = launch();
    await screen.findByRole("list", { name: "Паки" });
    expect(telegram.calls.ready).toBe(true);
    expect(telegram.calls.settingsShown).toBe(true);
    telegram.pressSettings();
    expect(await screen.findByRole("heading", { name: "Устройства" })).toBeTruthy();
  });

  it("opens the pack of a startapp link, and «Назад» from it leads to «Мои паки»", async () => {
    const { telegram } = launch({ startParam: "pack-cat_sofa_by_StickerSnapAppBot" });
    expect(await screen.findByRole("heading", { name: "Кот и диван" })).toBeTruthy();
    telegram.pressBack();
    expect(await screen.findByRole("heading", { name: "Мои паки" })).toBeTruthy();
  });
});
