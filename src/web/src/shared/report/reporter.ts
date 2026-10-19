import type { ReportedError } from "./errors";

/* «Сообщить об ошибке» opens the window «Написать отзыв» (features/feedback) from anywhere:
   popups, banners, the fallen screen. The window lives in a feature, so the app hands it over
   at start instead of the shared code importing it. */

let open: ((error: ReportedError) => void) | undefined;

export function setReportWindow(window: (error: ReportedError) => void) {
  open = window;
}

/** Opens «Написать отзыв» with the type «Ошибка» and the card of this error (19·4б). */
export function reportError(error: ReportedError) {
  open?.(error);
}
