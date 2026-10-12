import type { ValidationReport } from "../../shared/api/types";
import { t, type StringKey } from "../../shared/strings/ru";
import { kilobytes, num } from "../editor/format";

export type Rule = ValidationReport["checks"][number]["rule"];

export const RULE_LABEL: Record<Rule, StringKey> = {
  container: "validator.rule.container",
  codec: "validator.rule.codec",
  size: "validator.rule.size",
  dimensions: "validator.rule.dimensions",
  duration: "validator.rule.duration",
  fps: "validator.rule.fps",
  audio: "validator.rule.audio",
  alpha: "validator.rule.alpha",
};

const NONE = new Set(["none", "no", "false"]);

/** One side of a check as the server sends it («≤ 262144», «2.97», «none») in plain Russian:
 *  «до 256 КБ», «2,97 с», «нет». An unknown value is shown as it came. */
export function checkValue(
  rule: Rule,
  raw: string,
  side: "actual" | "expected" = "actual",
): string {
  const limit = /^\s*≤\s*/.exec(raw);
  const value = limit ? raw.slice(limit[0].length) : raw;
  const number = Number(value);
  const upTo = (text: string) => (limit ? t("validator.upTo", { value: text }) : text);

  switch (rule) {
    case "container":
    case "codec":
      return value.toUpperCase();
    case "size":
      return Number.isFinite(number) ? upTo(kilobytes(number)) : raw;
    case "duration":
      return Number.isFinite(number) ? upTo(t("editor.seconds", { value: num(number, 2) })) : raw;
    case "fps":
      return Number.isFinite(number) ? upTo(`${num(number)} fps`) : raw;
    case "dimensions":
      return Number.isFinite(number)
        ? t("validator.side", { side: num(number) })
        : value.replace(/\s*[x×]\s*/i, " × ");
    case "audio":
      if (value === "present") return t("validator.present");
      if (!NONE.has(value)) return value;
      return t(side === "expected" ? "validator.silent" : "validator.none");
    case "alpha":
      if (value === "any") return t("validator.any");
      return NONE.has(value) ? t("validator.none") : t("validator.present");
  }
}

/** How many rules failed, the Russian way: «1 правило», «2 правила», «5 правил». */
export function failedCount(report: ValidationReport): number {
  return report.checks.filter((c) => c.status === "fail").length;
}
