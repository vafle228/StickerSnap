import type { Concession, ExportResult, Job, JobItem, LadderStep } from "../../../shared/api/types";
import { t } from "../../../shared/strings/ru";
import { kilobytes, num } from "../format";

/* The words of artboard 3c for a render: what the job is doing, what was given up and how
   the size was reached. */

type Stage = Job["progress"]["stage"];

/** The segment waits for the ones before it: an export of all segments renders them in turn.
 *  A job's first item may still say «queued» before its first tick; it does not wait. */
export function waiting(job: Job, item?: JobItem): boolean {
  if (!item || item.state !== "queued") return false;
  const active = job.items.findIndex((i) => i.state !== "done");
  return job.items[active] !== item;
}

/** «Кодирую», the card's title while the export runs (3c, E). */
export function stageTitle(job: Job, item?: JobItem): string {
  const { stage } = job.progress;
  if (waiting(job, item)) return t("export.title.queued");
  if (stage === "probe") return t("export.title.probe");
  if (stage === "validate" || item?.state === "validating") return t("export.title.validate");
  return t("export.title.pass");
}

/** «кодирую», inside the popup's line «Стикер 2 · кодирую». */
export function stageWord(stage: Stage, item?: JobItem): string {
  if (stage === "probe") return t("export.stage.probe");
  if (stage === "validate" || item?.state === "validating") return t("export.stage.validate");
  return t("export.stage.pass");
}

/** «24 fps», «CRF 37», «сторона 448». */
export function concessionText(c: Concession): string {
  return t(`concession.${c.kind}`, { to: num(c.to) });
}

/** «Проход 2 из 2 · попытка 2: 24 fps» under the card's bar. */
export function stageLine(job: Job, item?: JobItem): string {
  if (waiting(job, item)) return t("export.line.queued");
  const { stage, attempt } = job.progress;
  const line =
    stage === "probe" || stage === "pass1" || stage === "pass2" || stage === "validate"
      ? t(`export.line.${stage}`)
      : t("export.line.pass2");
  if (!attempt || attempt.n < 2 || stage === "validate") return line;
  const what = attempt.concessions.map(concessionText).join(" + ");
  return `${line} · ${t("export.attempt", { n: attempt.n, what })}`;
}

/** The share of the job one segment has done, 0..100. */
export function percent(fraction: number): number {
  return Math.round(Math.max(0, Math.min(1, fraction)) * 100);
}

export interface Lost {
  label: string;
  value: string;
  /** Changed against the first attempt */
  given: boolean;
}

/** «Чем пожертвовали» (3c, H): smoothness, sharpness and size, «было → стало» or unchanged. */
export function lostOf(result: ExportResult): Lost[] {
  const find = (kind: Concession["kind"]) => result.concessions.find((c) => c.kind === kind);
  const fps = find("fps");
  const crf = find("crf");
  const scale = find("scale");
  const side = Math.max(result.output.width, result.output.height);
  return [
    {
      label: t("result.lost.fps"),
      value: fps
        ? t("result.from", { from: num(fps.from), to: `${num(fps.to)} fps` })
        : t("result.same", { value: `${num(result.output.fps)} fps` }),
      given: Boolean(fps),
    },
    {
      label: t("result.lost.crf"),
      value: crf
        ? t("result.from", { from: `CRF ${num(crf.from)}`, to: num(crf.to) })
        : t("result.same", { value: `CRF ${num(result.crf)}` }),
      given: Boolean(crf),
    },
    {
      label: t("result.lost.side"),
      value: scale
        ? t("result.from", { from: num(scale.from), to: num(scale.to) })
        : t("result.same", { value: num(side) }),
      given: Boolean(scale),
    },
  ];
}

/** The fps every attempt starts from: what the first one had. */
function baseFps(result: ExportResult): number {
  return result.concessions.find((c) => c.kind === "fps")?.from ?? result.output.fps;
}

/** One chip of «Как подбирали»: «20 fps + CRF 37 · 251 КБ». */
export function ladderText(step: LadderStep, result: ExportResult): string {
  const fps = step.label.find((c) => c.kind === "fps")?.to ?? baseFps(result);
  const rest = step.label.filter((c) => c.kind !== "fps").map(concessionText);
  const what = [t("result.ladderFps", { fps: num(fps) }), ...rest].join(" + ");
  return t("result.ladderStep", { what, size: kilobytes(step.bytes) });
}

/** The side was never cut: the ladder's last rung shows as «не понадобилось». */
export function sideUntouched(result: ExportResult): boolean {
  return !result.ladder.some((step) => step.label.some((c) => c.kind === "scale"));
}
