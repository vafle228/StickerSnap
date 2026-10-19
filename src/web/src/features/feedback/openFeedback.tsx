import { overlays } from "../../shared/overlays";
import type { ReportedError } from "../../shared/report";
import { FeedbackDialog } from "./FeedbackDialog";

/** Opens «Написать отзыв» (layer 2). From the menu and «О программе» it starts as «Идея». */
export function openFeedback() {
  overlays.openWindow((close) => <FeedbackDialog onClose={close} />);
}

/** «Сообщить об ошибке»: the same window as «Ошибка» with the error's card on top (19·4б). */
export function openBugReport(error: ReportedError) {
  overlays.openWindow((close) => <FeedbackDialog error={error} onClose={close} />);
}
