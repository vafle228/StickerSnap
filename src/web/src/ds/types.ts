import type { ReactNode } from "react";

/** Notification tones (artboard «8 · Оповещения»): Инфо, Готово, Предупреждение, Ошибка. Accent never recolors them. */
export type Tone = "info" | "success" | "warning" | "danger";

export const TONE_ICON = {
  info: "info",
  success: "check-circle",
  warning: "alert",
  danger: "error-circle",
} as const satisfies Record<Tone, string>;

/** A plain string is its own value and label. */
export type Item<V extends string = string> = V | { value: V; label: ReactNode };

export interface ItemObject<V extends string = string> {
  value: V;
  label: ReactNode;
}

export function itemsOf<V extends string>(items: readonly Item<V>[]): ItemObject<V>[] {
  return items.map((it) => (typeof it === "string" ? { value: it, label: it } : it));
}
