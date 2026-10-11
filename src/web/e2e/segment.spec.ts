import { expect, test } from "@playwright/test";

/* A segment from the keyboard (R2, R4, R16): I and O mark its edges, O stops at 3,00 s with the
   hint, «Вписать целиком» speeds the segment up so the whole marked span fits. */

test("marks a segment with I and O, stops at 3,00 s and fits the rest by speed", async ({
  page,
}) => {
  await page.goto("/?mock=ok");
  await page.getByRole("button", { name: /^Кот и клавиатура/ }).click();
  const timeline = page.getByRole("region", { name: "Таймлайн" });
  const inspector = page.getByRole("complementary", { name: "Инспектор" });

  await timeline.getByRole("button", { name: "Отрезок", exact: true }).click();
  await expect(timeline.getByRole("button", { name: "Стикер 3 · 3,00 с" })).toHaveAttribute(
    "aria-pressed",
    "true",
  );

  // I at the start, then 4 s ahead (120 frames at 30 fps) and O
  await page.keyboard.press("i");
  for (let frame = 0; frame < 120; frame++) await page.keyboard.press("ArrowRight");
  await expect(timeline.getByText("00:04.000 · кадр 120")).toBeVisible();
  await page.keyboard.press("o");

  const hint = timeline.getByRole("alert");
  await expect(hint).toContainText("Максимум 3,00 с при скорости 1×");
  await expect(timeline.getByText("O 00:03.000")).toBeVisible();

  await hint.getByRole("button", { name: "Вписать целиком" }).click();
  // O takes in frame 120 whole; 4,03 s at 1,35× plays in 2,99 s
  await expect(timeline.getByText("O 00:04.033")).toBeVisible();
  await expect(inspector.getByText("1,35×", { exact: true })).toBeVisible();
  await expect(timeline.getByRole("button", { name: "Стикер 3 · 2,99 с" })).toBeVisible();
  await expect(timeline.getByRole("alert")).toHaveCount(0);
});
