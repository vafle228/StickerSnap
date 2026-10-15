import { expect, test, type Page } from "@playwright/test";

/* «Мои паки» and publishing (R13–R15, artboard 7): a pack's stickers lead back to their
   projects, a sticker leaves the pack only after the confirmation, the publish window takes
   emoji per segment and a pack and sends them, and the pack states say what to do. */

async function openPacks(page: Page, scenario: string) {
  await page.goto(`/?mock=${scenario}`);
  await page.getByRole("button", { name: "Мои паки", exact: true }).click();
  return page.getByRole("navigation", { name: "Паки" });
}

test("opens a sticker's project on its own segment", async ({ page }) => {
  await openPacks(page, "ok");
  await page
    .getByRole("list", { name: "Стикеры пака" })
    .getByRole("button", { name: "Эмодзи: 🤔 Кот и клавиатура" })
    .click();

  await expect(page.getByRole("heading", { level: 1, name: "Кот и клавиатура" })).toBeVisible();
  const pressed = page.getByRole("group", { name: "Отрезки" }).locator("[aria-pressed=true]");
  await expect(pressed).toHaveText("Стикер 2 · 2,00 с");
});

test("removes a sticker from the pack only after the confirmation", async ({ page }) => {
  await openPacks(page, "ok");
  const pack = page.getByRole("region", { name: "Реакции с созвона" });
  await expect(pack).toContainText("Стикеры · 12 из 120");

  const sticker = pack.getByRole("listitem").filter({ hasText: "👋" });
  await sticker.getByRole("button", { name: "Действия со стикером" }).click();
  await page.getByRole("menuitem", { name: "Удалить из пака…" }).click();
  const confirm = page.getByRole("alertdialog", { name: "Удалить «👋» из пака?" });
  await confirm.getByRole("button", { name: "Удалить из пака" }).click();

  await expect(sticker).toHaveCount(0);
  await expect(pack).toContainText("Стикеры · 11 из 120");
});

test("closes the publish window by its cross and by «Отмена»", async ({ page }) => {
  await openPacks(page, "ok");
  const open = page.getByRole("button", { name: "Добавить стикер из любого проекта" });
  const publish = page.getByRole("dialog", { name: "Опубликовать в Telegram" });

  await open.click();
  await publish.getByRole("button", { name: "Закрыть" }).click();
  await expect(publish).toHaveCount(0);

  await open.click();
  await publish.getByRole("button", { name: "Отмена" }).click();
  await expect(publish).toHaveCount(0);
});

test("fills the publish window, sends, and ends with the pack to open in Telegram", async ({
  page,
}) => {
  await openPacks(page, "ok");
  await page.getByRole("button", { name: "Добавить стикер из любого проекта" }).click();
  const publish = page.getByRole("dialog", { name: "Опубликовать в Telegram" });
  await expect(publish).toContainText("Выберите эмодзи для «Стикер 1»");

  // A row with emoji shows them as chips, so the next «Выбрать эмодзи» is the first one left
  const pick = publish.getByRole("button", { name: "Выбрать эмодзи" });
  await pick.first().click();
  await page
    .getByRole("dialog", { name: "Эмодзи для «Стикер 1»" })
    .getByRole("button", { name: "😺" })
    .first()
    .click();
  await page.keyboard.press("Escape");
  await pick.first().click();
  await page
    .getByRole("dialog", { name: "Эмодзи для «Стикер 2»" })
    .getByRole("button", { name: "🔥" })
    .first()
    .click();
  await page.keyboard.press("Escape");
  await expect(publish.getByRole("button", { name: "Убрать 😺" })).toBeVisible();
  await expect(publish.getByRole("button", { name: "Убрать 🔥" })).toBeVisible();

  const where = publish.getByRole("radiogroup", { name: "В какой пак их добавить?" });
  await where.getByText("Кот и диван", { exact: true }).click();
  await expect(where.getByRole("radio", { name: /^Кот и диван/ })).toBeChecked();
  await expect(publish).toContainText("Заменит старую версию в «Кот и диван», место сохранится");

  await expect(publish).toContainText("Всё готово к отправке");
  await publish.getByRole("button", { name: "Опубликовать 2 стикера" }).click();

  // The window closes, the send popup follows the job to its end
  await expect(publish).toHaveCount(0);
  await expect(page.getByText("Готово: стикеры в «Кот и диван»")).toBeVisible({ timeout: 10_000 });
  await expect(page.getByRole("button", { name: "Открыть в Telegram" }).first()).toBeVisible();
});

test("offers no full pack in the publish window", async ({ page }) => {
  const packs = await openPacks(page, "pack-full");
  await expect(
    packs.getByRole("button", { name: /^Кот и диван Заполнен · 120 из 120/ }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Добавить стикер из любого проекта" }).click();
  const where = page
    .getByRole("dialog", { name: "Опубликовать в Telegram" })
    .getByRole("radiogroup", { name: "В какой пак их добавить?" });
  await expect(where.getByRole("radio", { name: /Кот и диван/ })).toBeDisabled();
});

test("removes a pack Telegram no longer has from the list", async ({ page }) => {
  const packs = await openPacks(page, "pack-missing");
  await packs.getByRole("button", { name: /^Первый тест Не найден в Telegram/ }).click();
  const gone = page.getByRole("alert").filter({ hasText: "«Первый тест» не найден в Telegram" });
  await gone.getByRole("button", { name: "Убрать из списка" }).click();

  await expect(packs.getByRole("button", { name: /^Первый тест/ })).toHaveCount(0);
  await expect(page.getByRole("status").filter({ hasText: "Пак убран из списка" })).toBeVisible();
});

test("asks to link Telegram first when no account is linked", async ({ page }) => {
  await openPacks(page, "unlinked");
  const link = page.getByRole("alert").filter({ hasText: "Сначала привяжите Telegram" });
  await expect(link.getByRole("button", { name: "Привязать аккаунт" })).toBeVisible();
});

test("shows a send Telegram asked to wait for, with «Повторить сейчас»", async ({ page }) => {
  const packs = await openPacks(page, "publish-errors");
  await expect(packs.getByRole("button", { name: /^Лето 2026 Отправляем… 2 из 4/ })).toBeVisible();
  const wait = page.getByRole("alert").filter({ hasText: "2 из 4 в паке, 2 ждут" });
  await expect(wait).toContainText("Telegram попросил подождать");
  await expect(wait.getByRole("button", { name: "Повторить сейчас" })).toBeVisible();
});

test("adds a pack of our bot by its link and refuses another bot's", async ({ page }) => {
  await openPacks(page, "ok");
  await page.getByRole("button", { name: "Добавить по ссылке…" }).click();
  const dialog = page.getByRole("dialog", { name: "Добавить по ссылке" });
  const field = dialog.getByRole("textbox", { name: "Ссылка на пак" });

  await field.fill("https://t.me/addstickers/Animals_by_OtherBot");
  await dialog.getByRole("button", { name: "Добавить" }).click();
  await expect(dialog).toContainText("Это не пак StickerSnap");

  await field.fill("t.me/addstickers/kot_divan_by_StickerSnapAppBot");
  await dialog.getByRole("button", { name: "Добавить" }).click();

  await expect(dialog).toHaveCount(0);
  await expect(page.getByText("Пак «Кот и диван» добавлен")).toBeVisible();
  await expect(page.getByRole("region", { name: "Кот и диван" })).toBeVisible();
});
