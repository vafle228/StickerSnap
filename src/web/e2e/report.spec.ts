import { expect, test } from "@playwright/test";

/* «Сообщить об ошибке» (artboard 19): a screen that fell, the crash of the last launch, and the
   window «Написать отзыв» they open with the error's card. */

test("replaces a fallen screen with «Что-то сломалось» and reports it with the steps before", async ({
  page,
}) => {
  await page.goto("/?mock=screen-crash");

  const crash = page.getByRole("alert").filter({ hasText: "Что-то сломалось" });
  await expect(crash).toContainText("Проект сохранён автоматически, ничего не потеряно.");
  await expect(crash).toContainText(/Код [0-9a-f]{4}-[0-9a-f]{4} · \d\d:\d\d/);
  // The menu on the left stays and works
  await expect(page.getByRole("button", { name: "Проекты", exact: true })).toBeVisible();

  const sent = page.waitForRequest(
    (r) => r.method() === "POST" && new URL(r.url()).pathname === "/api/v1/feedback",
  );
  await crash.getByRole("button", { name: "Сообщить об ошибке" }).click();
  const window = page.getByRole("dialog", { name: "Написать отзыв" });
  await expect(window.getByRole("radio", { name: "Ошибка" })).toBeChecked();
  await expect(window).toContainText(/internal · [0-9a-f]{4}-[0-9a-f]{4} · сегодня/);
  await expect(window).toContainText("Последние 30 действий в интерфейсе");
  await window.getByRole("button", { name: "Отправить" }).click();

  const body = (await sent).postDataJSON() as {
    type: string;
    error: { source: string; message: string };
    actions: { what: string }[];
  };
  expect(body.type).toBe("bug");
  expect(body.error).toMatchObject({
    source: "interface",
    message: expect.stringContaining("TypeError"),
  });
  expect(body.actions.map((a) => a.what)).toContain("Кнопка: Сообщить об ошибке");
  await expect(page.getByRole("status").filter({ hasText: "Отзыв отправлен" })).toBeVisible();

  await crash.getByRole("button", { name: "Перезапустить экран" }).click();
  await expect(crash).toHaveCount(0);
});

test("offers to report the crash of the last launch once, at the start", async ({ page }) => {
  await page.goto("/?mock=last-crash");

  const toast = page.getByRole("alert").filter({ hasText: "Прошлый запуск завершился ошибкой" });
  await expect(toast).toContainText("Сервер приложения остановился сам (код 3).");
  await toast.getByRole("button", { name: "Сообщить об ошибке" }).click();

  const window = page.getByRole("dialog", { name: "Написать отзыв" });
  await expect(window.getByText("Прошлый запуск завершился ошибкой")).toBeVisible();
  await expect(toast).toHaveCount(0);
});
