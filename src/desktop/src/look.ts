import type { ThemeSource } from "./bridge.js";

/** The window's colour before the SPA paints: token `bg` of tokens.json in each theme
 *  (look.test.ts keeps them in step). */
export const BACKGROUND = { light: "#f4f1ec", dark: "#16171c" } as const;

export function background(theme: ThemeSource, systemDark: boolean): string {
  const dark = theme === "dark" || (theme === "system" && systemDark);
  return dark ? BACKGROUND.dark : BACKGROUND.light;
}

export interface Size {
  width: number;
  height: number;
}

/** The artboards are drawn at 1440 × 900; smaller screens get the window that fits. */
export const DEFAULT_SIZE: Size = { width: 1440, height: 900 };
export const MIN_SIZE: Size = { width: 1024, height: 700 };

export function initialSize(workArea: Size): Size {
  return {
    width: Math.max(MIN_SIZE.width, Math.min(DEFAULT_SIZE.width, workArea.width)),
    height: Math.max(MIN_SIZE.height, Math.min(DEFAULT_SIZE.height, workArea.height)),
  };
}
