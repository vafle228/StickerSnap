import { expect, test } from "@playwright/test";

/* First launch (R1, R21): no projects yet, the start screen takes a video, «Читаю» shows while
   the file is read, and the editor opens on one 3-second segment. */

test("imports the first video and opens it in the editor with one segment", async ({ page }) => {
  await page.goto("/?mock=no-projects");
  await expect(page.getByRole("region", { name: "Недавние проекты" })).toHaveCount(0);

  const chooser = page.waitForEvent("filechooser");
  await page.getByRole("button", { name: /^Перетащите видео или GIF сюда/ }).click();
  await (
    await chooser
  ).setFiles({ name: "Кот прыгает.mp4", mimeType: "video/mp4", buffer: Buffer.from("video") });

  await expect(
    page.getByRole("status").filter({ hasText: "Читаю «Кот прыгает.mp4»" }),
  ).toBeVisible();
  await expect(page.getByRole("heading", { level: 1, name: "Кот прыгает" })).toBeVisible();
  const segments = page.getByRole("group", { name: "Отрезки" });
  await expect(segments.getByRole("button", { name: /^Стикер \d/ })).toHaveText([
    "Стикер 1 · 3,00 с",
  ]);

  // The new project is on top of «Проекты»
  await page.getByRole("button", { name: "Проекты", exact: true }).click();
  await expect(page.getByRole("button", { name: "Открыть проект «Кот прыгает»" })).toBeVisible();
});
