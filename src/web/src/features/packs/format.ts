import type { PackSummary } from "../../shared/api/types";
import { plural, t } from "../../shared/strings/ru";

export type PackTone = "normal" | "busy" | "warning";

/** The line under a pack's title in the list (artboard 7): how full it is, or what is wrong. */
export function packMeta(p: PackSummary): { text: string; tone: PackTone } {
  if (p.status === "missing") return { text: t("packs.missing"), tone: "warning" };
  if (p.sending) {
    return {
      text: t("packs.sending", { done: p.sending.done, total: p.sending.total }),
      tone: "busy",
    };
  }
  if (p.count >= p.limit) {
    return { text: t("packs.full", { count: p.count, limit: p.limit }), tone: "warning" };
  }
  return { text: t("packs.fill", { count: p.count, limit: p.limit }), tone: "normal" };
}

export const isFull = (p: Pick<PackSummary, "count" | "limit">) => p.count >= p.limit;

/** «Стикеры · 12 из 120» next to the open pack's title. */
export function packBadge(p: PackSummary): string {
  if (isFull(p)) return t("packs.full", { count: p.count, limit: p.limit });
  return t("packs.badge", { kind: t(`packs.kind.${p.type}`), count: p.count, limit: p.limit });
}

/** «Опубликовать 3 стикера» on the window's final button. */
export function sendLabel(n: number): string {
  if (n === 0) return t("publish.send.zero");
  return t(`publish.send.${plural(n)}`, { n });
}

/** Seconds left as «0:24». */
export function countdown(ms: number): string {
  const total = Math.max(0, Math.ceil(ms / 1000));
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, "0")}`;
}

/** A pack's link without the scheme, as the chip shows it. */
export const shortLink = (href: string) => href.replace(/^https?:\/\//, "");

const TINTS = ["lilac", "peach", "sky", "pink"] as const;
const SHAPES = ["999px", "20px", "999px", "16px", "20px"] as const;

/** Placeholder art of a sticker with no file to show: a tinted shape that differs from its
 *  neighbours, as the artboard draws them. Real files come from Telegram in stage 1c. */
export function placeholder(index: number): { tint: string; radius: string } {
  return {
    tint: `var(--tint-${TINTS[index % TINTS.length]})`,
    radius: SHAPES[index % SHAPES.length] as string,
  };
}
