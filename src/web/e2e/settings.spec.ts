import { expect, test } from "@playwright/test";

/* Settings (R20, R22): no «Сохранить», a change of the look is on screen at once; the table of
   contents leads to its section. */

test.beforeEach(async ({ page }) => {
  await page.goto("/?mock=ok");
  await page.getByRole("button", { name: "Настройки", exact: true }).click();
});

test("applies the theme and the accent at once", async ({ page }) => {
  const look = page.getByRole("region", { name: "Оформление" });
  const html = page.locator("html");

  await look.getByRole("radiogroup", { name: "Тема" }).getByText("Тёмная", { exact: true }).click();
  await expect(html).toHaveAttribute("data-theme", "dark");
  await look
    .getByRole("radiogroup", { name: "Акцент" })
    .getByRole("radio", { name: "Малина" })
    .click();
  await expect(html).toHaveClass("ss-accent-raspberry");

  await look
    .getByRole("radiogroup", { name: "Тема" })
    .getByText("Светлая", { exact: true })
    .click();
  await expect(html).toHaveAttribute("data-theme", "light");
});

test("leads from the table of contents to its section", async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 560 });
  const about = page.getByRole("region", { name: "О программе" });
  await expect(about).not.toBeInViewport();
  await page
    .getByRole("navigation", { name: "Разделы настроек" })
    .getByRole("link", { name: "О программе" })
    .click();
  await expect(about).toBeInViewport();

  await about.getByRole("button", { name: "Лицензии…" }).click();
  await expect(page.getByRole("dialog")).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog")).toHaveCount(0);
});
