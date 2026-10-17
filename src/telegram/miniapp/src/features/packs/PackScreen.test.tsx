// @vitest-environment happy-dom
import { launch } from "../../test/dom";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { BOT } from "../../mocks/data";

const CALL = `pack-reactions_call_by_${BOT}`;
const CALL_LINK = `t.me/addstickers/reactions_call_by_${BOT}`;

async function openSticker(n: number) {
  const tile = await screen.findByRole("button", { name: new RegExp(`^Стикер ${n}:`) });
  await userEvent.click(tile);
  return screen.getByRole("dialog", { name: "Просмотр стикера" });
}

async function askToDelete(n: number) {
  const sheet = await openSticker(n);
  await userEvent.click(within(sheet).getByRole("button", { name: "Удалить из пака…" }));
}

describe("a pack", () => {
  it("shows its link, its count and the grid of first frames", async () => {
    launch({ startParam: CALL });
    expect(await screen.findByRole("heading", { name: "Реакции с созвона" })).toBeTruthy();
    expect(screen.getByText("Стикеры · 12 из 120")).toBeTruthy();
    expect(screen.getByText(CALL_LINK)).toBeTruthy();
    expect(screen.getByText("12 стикеров")).toBeTruthy();
    const grid = screen.getByRole("list", { name: "Стикеры" });
    expect(within(grid).getAllByRole("button")).toHaveLength(12);
  });

  it("keeps «Добавить в Telegram» and «Поделиться» inside Telegram", async () => {
    const { telegram } = launch({ startParam: CALL });
    await userEvent.click(await screen.findByRole("button", { name: "Добавить в Telegram" }));
    await userEvent.click(screen.getByRole("button", { name: "Поделиться" }));
    expect(telegram.calls.telegramLinks[0]).toBe(`https://${CALL_LINK}`);
    expect(telegram.calls.telegramLinks[1]).toMatch(/^https:\/\/t\.me\/share\/url\?url=/);
    expect(telegram.calls.links).toEqual([]);
  });

  it("copies its link, and the chip says «Скопировано» for 2 s", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
    const { telegram } = launch({ startParam: CALL });
    await userEvent.click(await screen.findByRole("button", { name: /Скопировать ссылку/ }));
    expect(writeText).toHaveBeenCalledWith(`https://${CALL_LINK}`);
    expect(await screen.findByText("Скопировано")).toBeTruthy();
    expect(telegram.calls.haptics).toContain("copied");
    await waitFor(() => expect(screen.queryByText("Скопировано")).toBeNull(), { timeout: 3000 });
  });

  it("says a full pack takes nothing more", async () => {
    launch({ startParam: `pack-best_2025_by_${BOT}` });
    expect(await screen.findByText("120 стикеров · больше Telegram не примет")).toBeTruthy();
    expect(screen.getByText("Заполнен")).toBeTruthy();
  });
});

describe("the sticker sheet", () => {
  it("shows the sticker's number and emoji, and pages to its neighbours", async () => {
    const { telegram } = launch({ startParam: CALL });
    const sheet = await openSticker(3);
    expect(within(sheet).getByRole("heading", { name: "Стикер 3 из 12" })).toBeTruthy();
    expect(within(sheet).getByText("😴 💤")).toBeTruthy();
    await userEvent.click(within(sheet).getByRole("button", { name: "Следующий стикер" }));
    expect(within(sheet).getByRole("heading", { name: "Стикер 4 из 12" })).toBeTruthy();
    expect(telegram.calls.haptics.filter((h) => h === "select")).toHaveLength(2);
  });

  it("turns Telegram's vertical swipes off while it is open", async () => {
    const { telegram } = launch({ startParam: CALL });
    await openSticker(1);
    expect(telegram.calls.verticalSwipes).toBe(false);
    await userEvent.click(screen.getByRole("button", { name: "Закрыть" }));
    expect(telegram.calls.verticalSwipes).toBe(true);
  });

  it("closes on «Назад» before the screen does", async () => {
    const { telegram } = launch({ startParam: CALL });
    await openSticker(1);
    telegram.pressBack();
    await waitFor(() =>
      expect(screen.queryByRole("dialog", { name: "Просмотр стикера" })).toBeNull(),
    );
    expect(screen.getByRole("heading", { name: "Реакции с созвона" })).toBeTruthy();
  });
});

describe("deleting a sticker", () => {
  it("asks first, then removes it from the grid and says how many are left", async () => {
    const { telegram } = launch({ startParam: CALL });
    await askToDelete(12);
    const confirm = screen.getByRole("alertdialog", { name: "Удалить стикер из пака?" });
    expect(telegram.calls.haptics).toContain("confirm");
    await userEvent.click(within(confirm).getByRole("button", { name: "Удалить" }));
    expect(await screen.findByText("Стикер удалён из пака")).toBeTruthy();
    expect(screen.getByText("В паке осталось 11 стикеров.")).toBeTruthy();
    expect(screen.getByText("11 стикеров")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Стикер 12: ✅" })).toBeNull();
    expect(telegram.calls.haptics).toContain("done");
  });

  it("keeps the sticker when «Отмена» is pressed", async () => {
    launch({ startParam: CALL });
    await askToDelete(3);
    await userEvent.click(screen.getByRole("button", { name: "Отмена" }));
    expect(screen.getByText("12 стикеров")).toBeTruthy();
  });

  it("warns that the last sticker takes the pack, and returns to «Мои паки» after", async () => {
    const { db } = launch({ startParam: `pack-summer_2026_by_${BOT}` });
    const summer = db.packs.find((p) => p.title === "Лето 2026");
    if (!summer) throw new Error("no demo pack");
    summer.stickers = summer.stickers.slice(0, 1);
    await askToDelete(1);
    expect(
      screen.getByText("Это последний стикер: пак исчезнет из Telegram вместе с ним."),
    ).toBeTruthy();
    await userEvent.click(screen.getByRole("button", { name: "Удалить пак" }));
    expect(await screen.findByRole("heading", { name: "Мои паки" })).toBeTruthy();
    expect(screen.getByText("Пак исчез из Telegram вместе с ним.")).toBeTruthy();
    await waitFor(() => expect(screen.queryByRole("button", { name: /Лето 2026/ })).toBeNull());
  });

  it("says it was not deleted when Telegram does not answer, and «Повторить» tries again", async () => {
    const { db, telegram } = launch({ scenario: "delete-fails", startParam: CALL });
    await askToDelete(2);
    await userEvent.click(screen.getByRole("button", { name: "Удалить" }));
    expect(await screen.findByText("Стикер не удалён")).toBeTruthy();
    expect(screen.getByText("12 стикеров")).toBeTruthy();
    expect(telegram.calls.haptics).toContain("error");
    db.scenario = "ok";
    await userEvent.click(screen.getByRole("button", { name: "Повторить" }));
    expect(await screen.findByText("Стикер удалён из пака")).toBeTruthy();
  });
});

describe("a pack Telegram no longer knows", () => {
  it("can only leave the list", async () => {
    launch({ scenario: "pack-missing", startParam: `pack-first_test_by_${BOT}` });
    expect(await screen.findByText("Пака нет в Telegram")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Добавить в Telegram" })).toBeNull();
    await userEvent.click(screen.getByRole("button", { name: "Убрать из списка" }));
    expect(await screen.findByRole("heading", { name: "Мои паки" })).toBeTruthy();
    await waitFor(() => expect(screen.queryByRole("button", { name: /Первый тест/ })).toBeNull());
  });

  it("is not found when a link names a pack that is not this user's", async () => {
    launch({ startParam: "pack-nobody_by_StickerSnapAppBot" });
    expect(await screen.findByText("Пак не найден")).toBeTruthy();
    await userEvent.click(screen.getByRole("button", { name: "К моим пакам" }));
    expect(await screen.findByRole("heading", { name: "Мои паки" })).toBeTruthy();
  });
});
