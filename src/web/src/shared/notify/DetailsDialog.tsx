import { Button, Dialog } from "../../ds";
import { t } from "../strings/ru";
import styles from "./notify.module.css";

export interface ToastDetails {
  /** A line of ffprobe or the tail of the FFmpeg log */
  log?: string;
  traceId?: string;
}

export function DetailsDialog({
  details,
  onClose,
}: {
  details: ToastDetails;
  onClose: () => void;
}) {
  return (
    <Dialog
      title={t("common.details")}
      text={details.traceId ? t("common.traceId", { traceId: details.traceId }) : undefined}
      dismissible
      onClose={onClose}
      actions={
        <Button variant="secondary" data-autofocus onClick={onClose}>
          {t("common.close")}
        </Button>
      }
    >
      {details.log && <pre className={styles.log}>{details.log}</pre>}
    </Dialog>
  );
}
