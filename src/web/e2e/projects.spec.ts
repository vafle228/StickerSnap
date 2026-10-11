import { expect, test } from "@playwright/test";

/* «Проекты» (R21, artboard 6): rename with F2, remove from the list and bring it back,
   delete from disk only after the confirmation. */

test.beforeEach(async ({ page }) => {
  await page.goto("/?mock=ok");
  await page.getByRole("button", { name: "Проекты", exact: true }).click();
});

const card = (page: import("@playwright/test").Page, name: string) =>
  page
    .getByRole("article")
    .filter({ has: page.getByRole("button", { name: `Открыть проект «${name}»` }) });

test("renames a project with F2", async ({ page }) => {
  await page.getByRole("button", { name: "Открыть проект «Кот и клавиатура»" }).focus();
  await page.keyboard.press("F2");
  const field = page.getByRole("textbox", { name: "Название проекта" });
  await field.fill("Кот за работой");
  await field.press("Enter");

  await expect(page.getByRole("button", { name: "Открыть проект «Кот за работой»" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Открыть проект «Кот и клавиатура»" })).toHaveCount(
    0,
  );
});

test("removes a project from the list and brings it back with «Вернуть»", async ({ page }) => {
  await card(page, "Пёс на диване").getByRole("button", { name: "Действия с проектом" }).click();
  await page.getByRole("menuitem", { name: /^Убрать из списка/ }).click();

  await expect(page.getByRole("button", { name: "Открыть проект «Пёс на диване»" })).toHaveCount(0);
  await page.getByRole("button", { name: "Вернуть" }).click();
  await expect(page.getByRole("button", { name: "Открыть проект «Пёс на диване»" })).toBeVisible();
});

test("deletes a project from disk only after the confirmation", async ({ page }) => {
  const open = page.getByRole("button", { name: "Открыть проект «Пёс на диване»" });
  const trash = async () => {
    await card(page, "Пёс на диване").getByRole("button", { name: "Действия с проектом" }).click();
    await page.getByRole("menuitem", { name: "Удалить с диска…" }).click();
    return page.getByRole("alertdialog", { name: "Удалить «Пёс на диване» с диска?" });
  };

  await (await trash()).getByRole("button", { name: "Отмена" }).click();
  await expect(open).toBeVisible();

  await (await trash()).getByRole("button", { name: "Удалить" }).click();
  await expect(open).toHaveCount(0);
});

test("opens a project by a click on its cover", async ({ page }) => {
  const cover = card(page, "Пёс на диване").locator(".ss-checker");
  await cover.click({ force: true });
  await expect(page.getByRole("heading", { level: 1, name: "Пёс на диване" })).toBeVisible();
});
