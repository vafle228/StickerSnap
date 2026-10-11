import { expect, test } from "@playwright/test";

/* What the user sees when something is wrong (artboard 8, R12, R21): a damaged project, a
   source that is gone, no connection to Telegram. */

test("explains a damaged project and shows its log and error number", async ({ page }) => {
  await page.goto("/?mock=project-errors");
  await page.getByRole("button", { name: /^Реакции с созвона/ }).click();

  const toast = page.getByRole("alert").filter({ hasText: "Это не проект StickerSnap" });
  await expect(toast).toContainText("Файл повреждён или сделан другой программой.");
  await toast.getByRole("button", { name: "Подробности" }).click();

  const details = page.getByRole("dialog", { name: "Подробности" });
  await expect(details).toContainText(/Номер ошибки: \S+/);
  await expect(details).toContainText("scene.schemaVersion 7 is newer than 1");
  // The start screen stays: nothing half-open in the editor
  await expect(page.getByRole("region", { name: "Недавние проекты" })).toBeVisible();
});

test("keeps a project with a missing source and leads to a segment past the new end", async ({
  page,
}) => {
  await page.goto("/?mock=source-missing");
  await page.getByRole("button", { name: "Проекты", exact: true }).click();
  const card = page
    .getByRole("article")
    .filter({ has: page.getByRole("button", { name: "Открыть проект «Кот и клавиатура»" }) });
  await expect(card).toContainText("Исходник не найден");

  // A click on the cover opens it; the middle of the cover is «Указать исходник»
  await card.click({ position: { x: 30, y: 30 } });
  const missing = page.getByRole("alert").filter({ hasText: "Нет видео Кот и клавиатура.mp4" });
  await expect(missing).toContainText("Отрезки и настройки целы.");
  await expect(page.getByRole("button", { name: "Точное превью" })).toBeDisabled();

  const chooser = page.waitForEvent("filechooser");
  await missing.getByRole("button", { name: "Указать файл…" }).click();
  await (
    await chooser
  ).setFiles({ name: "Кот короче.mp4", mimeType: "video/mp4", buffer: Buffer.from("video") });

  const warning = page.getByRole("alert").filter({ hasText: "«Стикер 2» выходит за конец видео" });
  await warning.getByRole("button", { name: "Показать" }).click();
  await expect(missing).toHaveCount(0);
  await expect(
    page.getByRole("group", { name: "Отрезки" }).getByRole("button", { name: /^Стикер 2/ }),
  ).toHaveAttribute("aria-pressed", "true");
});

test("says Telegram is out of reach in Settings when there is no network", async ({ page }) => {
  await page.goto("/?mock=offline");
  await page.getByRole("button", { name: "Настройки", exact: true }).click();
  const bot = page.getByRole("region", { name: "Публикация в Telegram" });
  await expect(bot).toContainText("Нет связи с Telegram");
  await expect(bot).not.toContainText("Привязан");
});
