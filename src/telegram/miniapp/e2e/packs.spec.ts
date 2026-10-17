import { expect, test } from "@playwright/test";

/* The one critical path of the Mini App: from «Мои паки» into a pack, a sticker up close,
   its deletion behind the confirmation, and «Назад» through the stack. */

test("deletes a sticker from a pack and goes back to «Мои паки»", async ({ page }) => {
  await page.goto("/?mock");
  const packs = page.getByRole("list", { name: "Паки" });
  await expect(packs.getByRole("button")).toHaveCount(5);

  await packs.getByRole("button", { name: /Реакции с созвона/ }).click();
  await expect(page.getByRole("heading", { name: "Реакции с созвона" })).toBeVisible();
  await expect(page.getByText("12 стикеров")).toBeVisible();

  await page.getByRole("button", { name: "Стикер 3: 😴 💤" }).click();
  const sheet = page.getByRole("dialog", { name: "Просмотр стикера" });
  await expect(sheet.getByRole("heading", { name: "Стикер 3 из 12" })).toBeVisible();

  await sheet.getByRole("button", { name: "Удалить из пака…" }).click();
  const confirm = page.getByRole("alertdialog", { name: "Удалить стикер из пака?" });
  await expect(confirm.getByRole("button", { name: "Отмена" })).toBeFocused();
  await confirm.getByRole("button", { name: "Удалить" }).click();

  await expect(page.getByText("Стикер удалён из пака")).toBeVisible();
  await expect(page.getByText("В паке осталось 11 стикеров.")).toBeVisible();
  await expect(page.getByText("11 стикеров", { exact: true })).toBeVisible();

  // «Назад» in Telegram's header (the dev bar plays it outside Telegram)
  await page.getByRole("button", { name: "Назад" }).click();
  await expect(packs.getByRole("button", { name: /Реакции с созвона/ })).toContainText(
    "Стикеры · 11 из 120",
  );
});
