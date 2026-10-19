import { Button } from "@stickersnap/ds";
import { t } from "../strings/ru";
import type { ReportedError } from "./errors";
import { reportError } from "./reporter";

/** «Сообщить» of artboard 19·1 and 19·2: a ghost button after the action that fixes the problem.
 *  The short word and the bug say it on the button, the whole «Сообщить об ошибке» goes to the
 *  tooltip and to screen readers. */
export function ReportButton({ error, onClick }: { error: ReportedError; onClick?: () => void }) {
  return (
    <Button
      size="sm"
      variant="ghost"
      icon="bug"
      title={t("report.full")}
      aria-label={t("report.full")}
      onClick={() => {
        onClick?.();
        reportError(error);
      }}
    >
      {t("report.short")}
    </Button>
  );
}
