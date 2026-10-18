import { expect, test } from "@playwright/test";

/* Linking a computer: the desktop's link opens «Привязать этот компьютер?», Telegram asks for
   write access first, and the outcome leads on to «Мои паки». */

test("links a computer from the desktop's link", async ({ page }) => {
  await page.goto("/?mock&startapp=link_demo&write=ask");
  await expect(page.getByRole("heading", { name: "Привязать этот компьютер?" })).toBeVisible();
  await expect(page.getByText("DESKTOP-7F3K2Q")).toBeVisible();
  // Alone on the stack: Telegram's header says «Закрыть»
  await expect(page.getByRole("button", { name: "Закрыть" })).toBeVisible();

  await page.getByRole("button", { name: "Привязать" }).click();
  await expect(page.getByRole("heading", { name: "Компьютер привязан" })).toBeVisible();

  await page.getByRole("button", { name: "К моим пакам" }).click();
  await expect(page.getByRole("list", { name: "Паки" }).getByRole("button")).toHaveCount(5);
});
