import { expect, test, type Locator, type Page } from "@playwright/test";

/* The timeline at «Кадры» (R2, R16): frame by frame with the arrows, Shift+click picks frames,
   «Сделать отрезком» turns them into a segment. Demo videos of other rates and lengths too. */

async function openCat(page: Page, scenario: string) {
  await page.goto(`/?mock=${scenario}`);
  await page.getByRole("button", { name: /^Кот и клавиатура/ }).click();
  return page.getByRole("region", { name: "Таймлайн" });
}

async function zoomToFrames(timeline: Locator) {
  await timeline.getByRole("slider", { name: "Масштаб таймлайна" }).focus();
  await timeline.page().keyboard.press("End");
  await expect(timeline.getByRole("slider", { name: "Масштаб таймлайна" })).toHaveValue("1");
}

/** A click on the frame strip above the ruler label of `frame`; the frame must be on screen. */
async function clickFrame(timeline: Locator, frame: number, shift = false) {
  const strip = timeline.getByRole("group", { name: "Кадры источника" });
  const label = await strip.getByText(String(frame), { exact: true }).boundingBox();
  const box = await strip.boundingBox();
  if (!label || !box) throw new Error(`frame ${frame} is not on screen`);
  const page = timeline.page();
  if (shift) await page.keyboard.down("Shift");
  await page.mouse.click(label.x + label.width / 2, box.y + 20);
  if (shift) await page.keyboard.up("Shift");
}

test("picks frames with Shift+click and makes them a segment", async ({ page }) => {
  const timeline = await openCat(page, "ok");
  // Past «Стикер 1» (frames 0–89) to free frames
  for (let frame = 0; frame < 95; frame++) await page.keyboard.press("ArrowRight");
  await zoomToFrames(timeline);

  await clickFrame(timeline, 96);
  await clickFrame(timeline, 98, true);
  await expect(timeline.getByText("Выделено 3 кадра")).toBeVisible();

  await timeline.getByRole("button", { name: "Сделать отрезком" }).click();
  await expect(timeline.getByRole("button", { name: "Стикер 3 · 0,10 с" })).toHaveAttribute(
    "aria-pressed",
    "true",
  );
  await expect(timeline).toContainText(/I 00:03\.200\s*O 00:03\.300/);
});

test("steps a long 29,97 fps video frame by frame at «Кадры»", async ({ page }) => {
  const timeline = await openCat(page, "long-video");
  await expect(timeline.getByText("00:10.010 · кадр 300")).toBeVisible();
  await zoomToFrames(timeline);
  await expect(timeline.getByText("305", { exact: true })).toBeVisible();

  await page.keyboard.press("ArrowRight");
  await expect(timeline.getByText("00:10.043 · кадр 301")).toBeVisible();
  for (let frame = 0; frame < 30; frame++) await page.keyboard.press("ArrowRight");
  // The strip scrolls after the playhead: the frames around 331 are on screen
  await expect(timeline.getByText("00:11.044 · кадр 331")).toBeVisible();
  await expect(timeline.getByText("331", { exact: true })).toBeVisible();
});

test("opens a short GIF with its alpha and one segment over the whole clip", async ({ page }) => {
  const timeline = await openCat(page, "short-gif");
  await expect(page.getByText("GIF · 1,5 с · 12 fps · с альфой")).toBeVisible();
  await expect(timeline.getByRole("button", { name: /^Стикер \d/ })).toHaveText([
    "Стикер 1 · 1,50 с",
  ]);
  await expect(timeline).toContainText(/I 00:00\.000\s*O 00:01\.500/);
});
