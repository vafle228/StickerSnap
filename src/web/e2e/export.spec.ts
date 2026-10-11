import { readFile } from "node:fs/promises";
import { expect, test, type Page } from "@playwright/test";

/* Export (R6, R7, R9) and the .webm check (R8): the progress lives in a popup and in the
   inspector, the finished file downloads, and that very file passes the hard rules of the
   check when it is dropped back on the window. A broken file fails it with the rules it misses. */

/** Drops `file` on the window, as the user drags it in from the file manager. */
async function dropFile(page: Page, name: string, bytes: Buffer) {
  const data = await page.evaluateHandle(
    ({ name, bytes }) => {
      const transfer = new DataTransfer();
      transfer.items.add(new File([new Uint8Array(bytes)], name, { type: "video/webm" }));
      return transfer;
    },
    { name, bytes: [...bytes] },
  );
  await page.dispatchEvent("main", "dragenter", { dataTransfer: data });
  await page.dispatchEvent("main", "drop", { dataTransfer: data });
}

test("exports a sticker with progress, downloads it, and the file passes the check", async ({
  page,
}) => {
  await page.goto("/?mock=ok");
  await page.getByRole("button", { name: /^Кот и клавиатура/ }).click();
  const inspector = page.getByRole("complementary", { name: "Инспектор" });
  await expect(inspector).toContainText(/≈ \d+ КБ/);

  const download = page.waitForEvent("download");
  await inspector.getByRole("button", { name: "Экспорт WEBM" }).click();
  const popup = page.getByRole("status").filter({ hasText: "Экспорт «Стикер 1»" });
  await expect(popup.getByRole("progressbar")).toHaveCount(1);
  await expect(inspector.getByRole("progressbar", { name: "Экспорт" })).toHaveCount(1);

  const file = await download;
  expect(file.suggestedFilename()).toBe("Стикер 1.webm");
  const result = inspector.getByRole("region", { name: "Стикер 1.webm готов" });
  await expect(result).toContainText("512 × 512");
  await expect(result.getByRole("button", { name: "Проверка Telegram пройдена" })).toBeVisible();

  const path = await file.path();
  await dropFile(page, "Стикер 1.webm", await readFile(path));
  const check = page.getByRole("dialog", { name: "Проверка «Стикер 1.webm»" });
  // The demo records the file in real time with MediaRecorder, so its length drifts with the
  // browser's timers and may land just over 3 s («с оговоркой»); every hard rule passes.
  await expect(check).toContainText(/КонтейнерWEBM.*КодекVP9.*Размер кадра512 × 512/);
  await expect(check).not.toContainText("Не подходит");
});

test("fails a foreign .webm with the rules it misses and offers the editor", async ({ page }) => {
  await page.goto("/?mock=webm-bad");
  await dropFile(page, "чужой.webm", Buffer.from("webm"));

  const check = page.getByRole("dialog", { name: "Проверка «чужой.webm»" });
  await expect(check).toContainText("Не подходит");
  await expect(check).toContainText("не выполнено 4 правила");
  await expect(check.getByRole("button", { name: "Исправить в редакторе" })).toBeVisible();
});
