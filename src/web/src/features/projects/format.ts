import type { ProjectSummary } from "../../shared/api/types";
import { plural, t } from "../../shared/strings/ru";
import { num } from "../editor/format";

const DAY_MONTH = new Intl.DateTimeFormat("ru-RU", { day: "numeric", month: "long" });
const DAY_MONTH_YEAR = new Intl.DateTimeFormat("ru-RU", {
  day: "numeric",
  month: "long",
  year: "numeric",
});
const TIME = new Intl.DateTimeFormat("ru-RU", { hour: "2-digit", minute: "2-digit" });

const startOfDay = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();

/** When a project was last saved, the way a person says it: «12 минут назад», «сегодня, 14:05»,
 *  «вчера», «3 октября», and the year only when it is not this one. */
export function when(iso: string, now: Date = new Date()): string {
  const date = new Date(iso);
  const minutes = Math.floor((now.getTime() - date.getTime()) / 60_000);
  if (minutes < 1) return t("when.now");
  if (minutes < 60) return t(`when.minutes.${plural(minutes)}`, { n: minutes });
  const days = Math.round((startOfDay(now) - startOfDay(date)) / 86_400_000);
  if (days === 0) return t("when.today", { time: TIME.format(date) });
  if (days === 1) return t("when.yesterday");
  if (date.getFullYear() === now.getFullYear()) return DAY_MONTH.format(date);
  // «30 декабря 2025», without the «г.» Intl adds
  return DAY_MONTH_YEAR.format(date).replace(/\s*г\.$/, "");
}

/** «3 стикера», «5 эмодзи», «4 отрезка» when stickers and emoji are mixed. */
export function segmentCount(p: Pick<ProjectSummary, "segmentCount" | "preset">): string {
  const n = p.segmentCount;
  if (n === 0) return t("projects.none");
  if (p.preset === "emoji") return t("projects.emoji", { n });
  return t(`projects.${p.preset === "mixed" ? "segments" : "stickers"}.${plural(n)}`, { n });
}

/** The line under the name: what is in the project and when it changed, or the missing source. */
export function projectMeta(p: ProjectSummary, now?: Date): string {
  if (p.sourceStatus === "missing") return t("projects.missing");
  return t("projects.meta", { count: segmentCount(p), when: when(p.updatedAt, now) });
}

/** The chip on the cover: the longest segment, «2,8 с»; none without segments. */
export function durationChip(p: ProjectSummary): string | undefined {
  return p.segmentCount > 0 ? t("editor.seconds", { value: num(p.durationSec, 1) }) : undefined;
}
