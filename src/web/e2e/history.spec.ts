import { expect, test } from "@playwright/test";

/* Undo, redo and autosave (R16, plan «Undo / redo», «Автосохранение»): the header buttons and
   Ctrl+Z / Ctrl+Shift+Z walk the history, every step is saved by itself a second later, and a
   project opened again shows what was saved. */

test("undoes and redoes an edit, saves it by itself and keeps it after reopening", async ({
  page,
}) => {
  await page.goto("/?mock=ok");
  await page.getByRole("button", { name: /^Кот и клавиатура/ }).click();
  const header = page.locator("header");
  const tabs = page.getByRole("region", { name: "Таймлайн" });
  const undo = header.getByRole("button", { name: "Отменить (Ctrl+Z)" });
  const redo = header.getByRole("button", { name: "Повторить (Ctrl+Shift+Z)" });
  await expect(undo).toBeDisabled();
  await expect(header.getByText("Сохранено", { exact: true })).toBeVisible();

  await tabs.getByRole("button", { name: "Удалить «Стикер 2»" }).click();
  await expect(tabs.getByRole("button", { name: /^Стикер 2 ·/ })).toHaveCount(0);
  await expect(header.getByText("Сохраняю…")).toBeVisible();
  await expect(header.getByText("Сохранено", { exact: true })).toBeVisible();

  await undo.click();
  await expect(tabs.getByRole("button", { name: /^Стикер 2 ·/ })).toHaveAttribute(
    "aria-pressed",
    "true",
  );
  await page.keyboard.press("Control+Shift+KeyZ");
  await expect(tabs.getByRole("button", { name: /^Стикер 2 ·/ })).toHaveCount(0);
  await expect(redo).toBeDisabled();
  await expect(header.getByText("Сохранено", { exact: true })).toBeVisible();

  // Another project in between, so the first one is read again when it comes back
  const projects = page.getByRole("button", { name: "Проекты", exact: true });
  await projects.click();
  await page.getByRole("button", { name: "Открыть проект «Пёс на диване»" }).click();
  await expect(header).toContainText("Пёс на диване");
  await projects.click();
  await page.getByRole("button", { name: "Открыть проект «Кот и клавиатура»" }).click();
  await expect(tabs.getByRole("button", { name: /^Стикер 1 ·/ })).toBeVisible();
  await expect(tabs.getByRole("button", { name: /^Стикер 2 ·/ })).toHaveCount(0);
});

test("tells a failed save once, keeps «Не сохранено» and saves on the retry", async ({ page }) => {
  await page.clock.install();
  await page.goto("/?mock=save-fails");
  await page.getByRole("button", { name: /^Кот и клавиатура/ }).click();
  const header = page.locator("header");

  await page
    .getByRole("region", { name: "Таймлайн" })
    .getByRole("button", { name: "Удалить «Стикер 2»" })
    .click();
  await page.clock.runFor(1000);
  await expect(
    page.getByRole("alert").filter({ hasText: "Не получается сохранить проект" }),
  ).toHaveCount(1);
  await expect(header.getByText("Не сохранено")).toBeVisible();

  await page.clock.runFor(30_000);
  await expect(header.getByText("Сохранено", { exact: true })).toBeVisible();
});
