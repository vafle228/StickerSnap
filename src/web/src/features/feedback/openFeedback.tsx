import { overlays } from "../../shared/overlays";
import { FeedbackDialog } from "./FeedbackDialog";

/** Opens «Написать отзыв» (layer 2). From the menu and «О программе» it starts as «Идея». */
export function openFeedback() {
  overlays.openWindow((close) => <FeedbackDialog onClose={close} />);
}
