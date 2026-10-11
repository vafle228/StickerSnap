import { expect, test } from "@playwright/test";

/* Motion (docs/ui-motion.md): an animation never blocks. Esc in the middle of a window's entry
   closes it, and the focus is back on its button at once, not after the fade. */

test.use({ reducedMotion: "no-preference" });

test("closes a window with Esc while it is still coming in", async ({ page }) => {
  await page.goto("/?mock=ok");
  await page.getByRole("button", { name: "Настройки" }).click();
  const licenses = page.getByRole("button", { name: "Лицензии…" });
  await licenses.click();

  const dialog = page.getByRole("dialog");
  await expect(dialog).toBeAttached();
  await page.keyboard.press("Escape");

  await expect(licenses).toBeFocused();
  await expect(page.locator(".ss-overlay")).toHaveCount(0);
});

test("lets a closed toast leave and closes its place in the stack", async ({ page }) => {
  await page.goto("/?mock=ok");
  await page.getByRole("button", { name: "Настройки" }).click();
  await page.getByRole("button", { name: "Скопировать версии" }).click();

  const toast = page.locator(".ss-toast-slot");
  await expect(toast).toHaveCount(1);
  await toast.getByRole("button", { name: "Закрыть" }).click();
  await expect(toast).toHaveAttribute("data-state", "closed");
  await expect(toast).toHaveCount(0);
});
