import type { ErrorCode, ErrorSource, Problem } from "../api/types";

/* The errors the user may report (artboard 19): a server's error with `reportable`, an error of
   the interface itself, the crash of the last launch. The same error again within 10 minutes is
   one report with a count, not a new one (19·4, «Результат»). */

/** An error as the window «Написать отзыв» shows it on its card and sends it (FeedbackError). */
export interface ReportedError {
  code: ErrorCode;
  /** The card's title in the interface language: «Не удалось опубликовать» */
  title: string;
  /** The server's trace id, or the number the interface gave its own error: 7f3a-c21e */
  traceId: string;
  source: ErrorSource;
  /** The interface's and the shell's own errors: the exception's text, why it stopped */
  message?: string;
  /** The stack and the component stack, or the tail of the server's output */
  stack?: string;
  /** ISO time it first happened */
  at: string;
  /** How many times it happened in the 10 minutes since */
  count: number;
}

export type ErrorInput = Omit<ReportedError, "at" | "count" | "traceId"> & { traceId?: string };

export const REPEAT_MS = 10 * 60_000;

/** The contract's bounds of FeedbackError */
export const MESSAGE_MAX = 1000;
export const STACK_MAX = 8000;
const COUNT_MAX = 1000;

const seen = new Map<string, ReportedError>();

/** One error, whatever request or render it came from: the same code, source and text. */
const keyOf = (error: ErrorInput) =>
  [error.source, error.code, error.message ?? error.title].join("|");

/** A number for an error the server never saw: 7f3a-c21e, to name it in a conversation. */
export function ownNumber(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(4));
  const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("");
  return `${hex.slice(0, 4)}-${hex.slice(4)}`;
}

/** The number as the card shows it: short enough to say, the start of the whole one. */
export function shortNumber(traceId: string): string {
  if (traceId.length <= 12) return traceId;
  const hex = traceId.replace(/[^0-9a-z]/gi, "").toLowerCase();
  return `${hex.slice(0, 4)}-${hex.slice(4, 8)}`;
}

/** Notes an error and returns it as it should be reported: the first time it happened, the
 *  latest number, how many times in 10 minutes. */
export function captureError(input: ErrorInput, now: Date = new Date()): ReportedError {
  const key = keyOf(input);
  const before = seen.get(key);
  // The same answer drawn again (a popup updated in place) is not a new occurrence
  if (before && input.traceId && before.traceId === input.traceId) return before;
  const fresh = before && now.getTime() - new Date(before.at).getTime() < REPEAT_MS;
  // The interface's own error keeps its first number: the one the user may already have named
  const traceId = input.traceId || (fresh ? before.traceId : ownNumber());
  const error: ReportedError = {
    ...input,
    message: input.message?.slice(0, MESSAGE_MAX),
    stack: input.stack?.slice(0, STACK_MAX),
    traceId,
    at: fresh ? before.at : now.toISOString(),
    count: fresh ? Math.min(before.count + 1, COUNT_MAX) : 1,
  };
  seen.set(key, error);
  return error;
}

/** The error behind a server's Problem, when the user may report it; undefined otherwise. */
export function captureProblem(problem: Problem, title: string): ReportedError | undefined {
  if (!problem.reportable) return undefined;
  // No trace: the server never answered with this error, the interface failed on its own
  const own = !problem.traceId;
  return captureError({
    code: problem.code,
    title,
    traceId: problem.traceId || undefined,
    source: own ? "interface" : "server",
    message: own ? problem.title : undefined,
  });
}

/** Forgets every error: for tests. */
export function forgetErrors() {
  seen.clear();
}
