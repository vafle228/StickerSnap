import { readFile } from "node:fs/promises";
import { expect, test, type Page } from "@playwright/test";
import { clip } from "./server";

/* The path the app exists for, on the real server with FFmpeg and the production SPA: a video
   goes in, the editor shows it with its frames and a size forecast, an edit is saved, the exact
   preview plays the server's draft, the export downloads a WEBM that passes Telegram's check,
   and the project is still there after a reload. Job progress comes over the hub, not polling. */

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

/** True once the <video> of the editor has a picture to show. */
async function playable(page: Page) {
  return page
    .locator("main video")
    .first()
    .evaluate((video: HTMLVideoElement) => {
      return video.readyState >= HTMLMediaElement.HAVE_CURRENT_DATA && video.videoWidth > 0;
    });
}

test("imports a video, edits it, previews it exactly and exports a sticker Telegram accepts", async ({
  page,
}) => {
  // Job polling is the fallback for a hub that is down; here every update must come over it.
  const polled: string[] = [];
  page.on("request", (request) => {
    if (/\/api\/v1\/jobs\/[^/]+$/.test(new URL(request.url()).pathname)) polled.push(request.url());
  });

  await page.goto("/");
  // The production build: no dev bar, no demo data
  await expect(page.getByRole("region", { name: "Режим разработки" })).toHaveCount(0);

  const chooser = page.waitForEvent("filechooser");
  await page.getByRole("button", { name: /^Перетащите видео или GIF сюда/ }).click();
  await (await chooser).setFiles(clip);

  await expect(page.getByRole("heading", { level: 1, name: "clip" })).toBeVisible();
  await expect(page.getByRole("group", { name: "Отрезки" }).getByRole("button")).toContainText([
    "Стикер 1 · 3,00 с",
  ]);
  await expect(page.getByText("MP4 · 4,0 с · 30 fps · без альфы")).toBeVisible();
  // The server's playable copy of the source (MPEG-4 is not for <video>)
  await expect.poll(() => playable(page)).toBe(true);

  const inspector = page.getByRole("complementary", { name: "Инспектор" });
  await expect(inspector).toContainText(/≈ \d+ КБ/);

  // An edit: the whole picture instead of the centre square, saved to the server
  const saved = page.waitForResponse(
    (response) => response.request().method() === "PUT" && response.url().endsWith("/scene"),
  );
  await inspector.getByRole("radio", { name: "Как в источнике" }).click();
  await expect(inspector).toContainText("512 × 288");
  expect((await saved).status()).toBe(200);
  await expect(page.getByText("Сохранено")).toBeVisible();

  await page.getByRole("button", { name: "Точное превью" }).click();
  await expect(page.getByText(/^Цикл · точное · \d+ КБ$/)).toBeVisible();

  const download = page.waitForEvent("download");
  await inspector.getByRole("button", { name: "Экспорт WEBM" }).click();
  const file = await download;
  expect(file.suggestedFilename()).toBe("Стикер 1.webm");

  const result = inspector.getByRole("region", { name: "Стикер 1.webm готов" });
  await expect(result).toContainText("512 × 288");
  await expect(result).toContainText(/\d+ КБ из 256/);
  await expect(result.getByRole("button", { name: "Проверка Telegram пройдена" })).toBeVisible();
  expect(polled).toEqual([]);

  // The very file that downloaded passes the server's check of Telegram's rules
  await dropFile(page, "Стикер 1.webm", await readFile(await file.path()));
  const check = page.getByRole("dialog", { name: "Проверка «Стикер 1.webm»" });
  await expect(check).toContainText(/КонтейнерWEBM.*КодекVP9.*Размер кадра512 × 288/);
  await expect(check).not.toContainText("Не подходит");
  await page.keyboard.press("Escape");

  // After a reload the project is in «Проекты» with its cover, and opens with the edit and the export
  await page.reload();
  await page.getByRole("button", { name: "Проекты", exact: true }).click();
  const card = page.getByRole("button", { name: "Открыть проект «clip»" });
  await expect(card).toBeVisible();
  const cover = page.getByRole("article").filter({ has: card }).locator("img");
  await expect
    .poll(() => cover.evaluate((img: HTMLImageElement) => img.naturalWidth))
    .toBeGreaterThan(1);

  await card.click();
  await expect(page.getByRole("heading", { level: 1, name: "clip" })).toBeVisible();
  await expect(inspector).toContainText("512 × 288");
  await expect(inspector.getByRole("region", { name: "Стикер 1.webm готов" })).toBeVisible();
});
