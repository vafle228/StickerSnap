import type { Job, Segment } from "../../../shared/api/types";
import type { ToastOptions } from "../../../shared/notify";
import { problemText, toProblem } from "../../../shared/notify";
import { captureProblem } from "../../../shared/report";
import { plural, t } from "../../../shared/strings/ru";
import { kilobytes } from "../format";
import { percent, stageWord } from "./format";

/* The export's popup of artboard 3c, F: the progress turns into the result in the same place.
   One popup per job, however many segments it has («Экспорт 2 из 3»). */

export type ExportToast = Pick<
  ToastOptions,
  "tone" | "title" | "badge" | "description" | "progress" | "details" | "report" | "timeout"
>;

const nameOf = (segments: Segment[], id: string) => segments.find((s) => s.id === id)?.name ?? id;

/** What the popup says for the job as it is now; undefined once there is nothing to say. */
export function exportToast(job: Job, segments: Segment[]): ExportToast | undefined {
  const total = job.items.length;
  if (job.state === "queued" || job.state === "running") {
    const index = Math.max(
      0,
      job.items.findIndex((i) => i.state !== "done"),
    );
    const item = job.items[index];
    const name = item ? nameOf(segments, item.segmentId) : "";
    return {
      tone: "info",
      title:
        total === 1
          ? t("export.toast.one", { name })
          : t("export.toast.many", { i: index + 1, n: total }),
      description: t("export.toast.line", { name, stage: stageWord(job.progress.stage, item) }),
      progress: percent(total === 1 ? (item?.fraction ?? 0) : job.progress.fraction),
    };
  }

  if (job.state === "succeeded" || job.state === "partial") {
    const results = job.items.flatMap((i) => (i.export ? [i.export] : []));
    const first = results[0];
    if (!first) return undefined;
    const given = results.some((r) => r.concessions.length > 0);
    const badge = given ? { tone: "warning" as const, label: t("export.concessions") } : undefined;
    if (results.length === 1) {
      return {
        // Made as asked, but Telegram will not take it: a result with a caveat (artboard 8)
        tone: first.validation.verdict === "fail" ? "warning" : "success",
        title: t("export.done.one", { file: `${nameOf(segments, first.segmentId)}.webm` }),
        badge,
        description: t(`export.done.${first.validation.verdict}`, { size: kilobytes(first.bytes) }),
      };
    }
    const failed = results.some((r) => r.validation.verdict === "fail");
    return {
      tone: failed ? "warning" : "success",
      title: t(`export.done.many.${plural(results.length)}`, { n: results.length }),
      badge,
      description: t(failed ? "export.done.someFail" : "export.done.allPass"),
    };
  }

  if (job.state === "failed") {
    const failure = job.error ?? toProblem(new Error(`job ${job.id} failed without a problem`));
    return {
      tone: "danger",
      title: t("export.failed"),
      description: problemText(failure).title,
      details:
        failure.log || failure.traceId
          ? { log: failure.log, traceId: failure.traceId || undefined }
          : undefined,
      report: captureProblem(failure, t("export.failed")),
    };
  }
  return undefined;
}
