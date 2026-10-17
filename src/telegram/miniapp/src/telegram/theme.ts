import { useLayoutEffect } from "react";
import type { Telegram } from "./bridge";

/** Variant A of artboard 16: from Telegram only its light or dark theme, the colours are the
 *  design system's. Follows themeChanged live and paints Telegram's frame in our background. */
export function applyTheme(telegram: Telegram, root: HTMLElement = document.documentElement) {
  root.setAttribute("data-theme", telegram.colorScheme());
  const background = getComputedStyle(root).getPropertyValue("--bg").trim();
  if (background) telegram.paint(background);
}

export function useTelegramTheme(telegram: Telegram) {
  useLayoutEffect(() => {
    applyTheme(telegram);
    return telegram.onThemeChanged(() => applyTheme(telegram));
  }, [telegram]);
}
