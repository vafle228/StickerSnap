import type { Tone } from "@stickersnap/ds";
import { ApiError } from "../api/client";
import type { ErrorCode, Problem } from "../api/types";
import { captureProblem } from "../report/errors";
import { t } from "../strings/ru";
import { notify, type ToastAction, type ToastHandle } from "./notify";

/** Where a problem shows up (contract, section «Ошибки», after artboard 8):
 *  toast — a popup; banner — a strip on the screen; field — a hint under the field;
 *  mark — a label on a row or a pack. Only toasts are drawn here, the rest by the screen. */
export type ProblemForm = "toast" | "banner" | "field" | "mark";

export const PROBLEMS: Record<ErrorCode, { tone: Tone; form: ProblemForm }> = {
  source_unreadable: { tone: "danger", form: "toast" },
  format_unsupported: { tone: "warning", form: "toast" },
  source_not_found: { tone: "danger", form: "toast" },
  project_not_found: { tone: "danger", form: "toast" },
  project_invalid: { tone: "danger", form: "toast" },
  project_too_new: { tone: "danger", form: "toast" },
  save_failed: { tone: "danger", form: "toast" },
  revision_conflict: { tone: "warning", form: "toast" },
  disk_full: { tone: "danger", form: "toast" },
  render_failed: { tone: "danger", form: "toast" },
  cannot_fit: { tone: "danger", form: "toast" },
  ffmpeg_missing: { tone: "danger", form: "toast" },
  job_not_found: { tone: "danger", form: "toast" },
  file_not_found: { tone: "danger", form: "toast" },
  validation_failed: { tone: "danger", form: "mark" },
  account_not_linked: { tone: "warning", form: "banner" },
  bot_blocked: { tone: "danger", form: "banner" },
  network_unavailable: { tone: "danger", form: "banner" },
  bot_unavailable: { tone: "danger", form: "banner" },
  pack_name_invalid: { tone: "warning", form: "field" },
  pack_name_taken: { tone: "danger", form: "field" },
  pack_full: { tone: "warning", form: "mark" },
  pack_not_found: { tone: "warning", form: "banner" },
  not_our_pack: { tone: "warning", form: "field" },
  rate_limited: { tone: "warning", form: "toast" },
  telegram_error: { tone: "danger", form: "toast" },
  bad_request: { tone: "danger", form: "toast" },
  internal: { tone: "danger", form: "toast" },
  link_unsupported: { tone: "danger", form: "field" },
  video_unavailable: { tone: "danger", form: "field" },
  site_unreachable: { tone: "danger", form: "toast" },
  site_changed: { tone: "danger", form: "toast" },
};

/** The Problem behind any thrown value. A failure without one (no server, a 401, a bug in the
 *  SPA) reads as `internal`: the user sees «Что-то пошло не так», the log gets the cause. */
export function toProblem(error: unknown): Problem {
  if (error instanceof ApiError && error.problem) return error.problem;
  return {
    type: "about:blank",
    status: error instanceof ApiError ? error.status : 0,
    title: error instanceof Error ? error.message : String(error),
    code: "internal",
    reportable: true,
    traceId: "",
  };
}

/** Title and text in the interface language, with params filled in. */
export function problemText(problem: Problem): { title: string; text: string } {
  return {
    title: t(`error.${problem.code}.title`, problem.params),
    text: t(`error.${problem.code}.text`, problem.params),
  };
}

export interface ProblemToastOptions {
  /** «Повторить» and the like */
  action?: ToastAction;
  /** One toast per key */
  key?: string;
}

/** Shows the popup for a failed request. Returns undefined when the code belongs to a banner,
 *  a field or a mark: the screen draws those from its own state. */
export function notifyProblem(
  error: unknown,
  options: ProblemToastOptions = {},
): ToastHandle | undefined {
  const problem = toProblem(error);
  const { tone, form } = PROBLEMS[problem.code];
  if (form !== "toast") return undefined;
  const { title, text } = problemText(problem);
  const details =
    problem.log || problem.traceId
      ? { log: problem.log, traceId: problem.traceId || undefined }
      : undefined;
  const report = captureProblem(problem, title);
  return notify.toast({ tone, title, description: text, details, report, ...options });
}
