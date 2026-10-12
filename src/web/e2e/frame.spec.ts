import { expect, test, type Locator, type Page } from "@playwright/test";

/* The sticker's frame (R3): «Рамка» on the canvas keeps the square unless Shift is held,
   «Готово» puts the result in the inspector; «Растянуть» takes sizes and its presets bring the
   proportion back. */

async function openCat(page: Page) {
  await page.goto("/?mock=ok");
  await page.getByRole("button", { name: /^Кот и клавиатура/ }).click();
  return {
    canvas: page.getByRole("region", { name: "Холст" }),
    inspector: page.getByRole("complementary", { name: "Инспектор" }),
  };
}

async function dragBy(page: Page, handle: Locator, dx: number, dy: number) {
  const box = await handle.boundingBox();
  if (!box) throw new Error("the handle is not on screen");
  const x = box.x + box.width / 2;
  const y = box.y + box.height / 2;
  await page.mouse.move(x, y);
  await page.mouse.down();
  await page.mouse.move(x + dx, y + dy, { steps: 6 });
  await page.mouse.up();
}

test("shrinks the crop as a square, frees it with Shift and lands it with «Готово»", async ({
  page,
}) => {
  const { canvas, inspector } = await openCat(page);
  await canvas.getByRole("button", { name: "Рамка", exact: true }).click();
  const frame = canvas.getByRole("group", { name: /^Рамка кропа/ });
  await expect(frame).toContainText("360 × 360 → 512 × 512");

  const corner = frame.getByRole("button", { name: "Правый нижний угол рамки" });
  await dragBy(page, corner, -60, -60);
  const square = (await frame.textContent())?.match(/(\d+) × (\d+) → 512 × 512/);
  expect(square?.[1]).toBe(square?.[2]);
  expect(Number(square?.[1])).toBeLessThan(360);

  await page.keyboard.down("Shift");
  await dragBy(page, corner, -60, 0);
  await page.keyboard.up("Shift");
  const free = (await frame.textContent())?.match(/(\d+) × (\d+) → (\d+) × 512/);
  expect(Number(free?.[1])).toBeLessThan(Number(free?.[2]));

  await canvas.getByRole("button", { name: "Готово" }).click();
  await expect(frame).toHaveCount(0);
  await expect(inspector).toContainText(new RegExp(`Выход\\s*${free?.[3]} × 512`));
  await expect(inspector.getByRole("button", { name: "Применить ко всем стикерам" })).toBeVisible();
});

test("stretches to typed sizes, and «Как в источнике» brings the proportion back", async ({
  page,
}) => {
  const { inspector } = await openCat(page);
  await inspector.getByRole("radio", { name: "Растянуть" }).click();
  const width = inspector.getByRole("textbox", { name: "Ширина" });
  const height = inspector.getByRole("textbox", { name: "Высота" });
  await expect(width).toHaveValue("512");
  await expect(height).toHaveValue("288");

  // Free: the proportion may break on purpose
  await height.fill("512");
  await height.press("Enter");
  await expect(width).toHaveValue("512");
  await expect(height).toHaveValue("512");

  await expect(inspector.getByRole("switch", { name: /^Закрепить пропорции/ })).toHaveCount(0);
  await inspector.getByRole("radio", { name: "Как в источнике" }).click();
  await expect(height).toHaveValue("288");
});
