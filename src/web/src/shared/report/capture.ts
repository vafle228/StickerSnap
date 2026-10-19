import type { LastCrash } from "@stickersnap/desktop/bridge";
import { ApiError } from "../api/client";
import { notify } from "../notify/notify";
import { t } from "../strings/ru";
import { captureError, type ErrorInput, type ReportedError } from "./errors";
import { describeClick, trail } from "./trail";

/* What the interface collects for a bug's report before anyone asks (artboard 19, section 4):
   clicks for the trail, the errors nothing caught, the crash of the last launch. */

/** Noise of the browser, not an error of the app */
const BENIGN = [/ResizeObserver loop/i];

/** An error nothing caught, as the report takes it; undefined for noise and for a server's error
 *  the user cannot fix by reporting (reportable: false). */
export function uncaught(reason: unknown): ErrorInput | undefined {
  if (reason instanceof ApiError && reason.problem && !reason.problem.reportable) return undefined;
  const error = reason instanceof Error ? reason : undefined;
  const message = error ? `${error.name}: ${error.message}` : String(reason);
  if (BENIGN.some((pattern) => pattern.test(message))) return undefined;
  return {
    code: "internal",
    title: t("error.internal.title"),
    source: "interface",
    message,
    stack: error?.stack,
  };
}

/** The popup of an error nothing caught: one per error, its count grows in place. */
export function notifyUncaught(input: ErrorInput) {
  const error = captureError(input);
  notify.toast({
    key: `uncaught:${input.message ?? ""}`,
    tone: "danger",
    title: t("error.internal.title"),
    description: t("report.uncaught.text"),
    report: error,
  });
  return error;
}

/** Starts the trail of clicks and the catch of uncaught errors; returns the stop. */
export function startCapture(target: Window = window): () => void {
  const onClick = (event: MouseEvent) => {
    const what = describeClick(event.target);
    if (what) trail.record(what);
  };
  const onError = (event: ErrorEvent) => {
    const input = uncaught(event.error ?? event.message);
    if (input) notifyUncaught(input);
  };
  const onRejection = (event: PromiseRejectionEvent) => {
    const input = uncaught(event.reason);
    if (input) notifyUncaught(input);
  };
  target.addEventListener("click", onClick, { capture: true });
  target.addEventListener("error", onError);
  target.addEventListener("unhandledrejection", onRejection);
  return () => {
    target.removeEventListener("click", onClick, { capture: true });
    target.removeEventListener("error", onError);
    target.removeEventListener("unhandledrejection", onRejection);
  };
}

/** The crash of the last launch as the report takes it. */
export function crashError(crash: LastCrash): ReportedError {
  const message =
    crash.kind === "server"
      ? `The server stopped with code ${String(crash.code ?? "unknown")}`
      : crash.kind === "window"
        ? `The window's renderer is gone: ${crash.reason ?? "unknown"}`
        : "The app ended without quitting";
  return captureError(
    {
      code: "internal",
      title: t(`report.crash.${crash.kind}`),
      source: "shell",
      message,
      stack: crash.output,
    },
    new Date(crash.at),
  );
}

/** «Прошлый запуск завершился ошибкой» with «Сообщить», at the start after a crash. */
export function notifyCrash(crash: LastCrash) {
  const error = crashError(crash);
  notify.toast({
    key: "last-crash",
    tone: "danger",
    title: t(`report.crash.${crash.kind}`),
    description: t(`report.crash.${crash.kind}.text`, { code: String(crash.code ?? "—") }),
    report: error,
  });
  return error;
}
