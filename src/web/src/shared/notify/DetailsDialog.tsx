import { useEffect, useState } from "react";
import { Button, Dialog } from "../../ds";
import { t } from "../strings/ru";
import styles from "./notify.module.css";

export interface ToastDetails {
  /** A line of ffprobe or the tail of the FFmpeg log */
  log?: string;
  traceId?: string;
}

/** How long «Скопировано» stands on the button (artboard 8: confirmed in place, no popup). */
const COPIED_MS = 2000;

/** «Подробности» of an error (artboard 3c, B): the log in mono and «Скопировать» for a report. */
export function DetailsDialog({
  details,
  onClose,
}: {
  details: ToastDetails;
  onClose: () => void;
}) {
  const [copied, setCopied] = useState(false);
  const text = [details.log, details.traceId && t("common.traceId", { traceId: details.traceId })]
    .filter(Boolean)
    .join("\n");

  useEffect(() => {
    if (!copied) return undefined;
    const timer = setTimeout(() => setCopied(false), COPIED_MS);
    return () => clearTimeout(timer);
  }, [copied]);

  const copy = () => {
    void navigator.clipboard?.writeText(text).then(
      () => setCopied(true),
      () => {},
    );
  };

  return (
    <Dialog
      title={t("common.details")}
      text={details.traceId ? t("common.traceId", { traceId: details.traceId }) : undefined}
      dismissible
      onClose={onClose}
      actions={
        <>
          {text && (
            <Button variant="ghost" icon={copied ? "check" : "copy"} onClick={copy}>
              {t(copied ? "common.copied" : "common.copy")}
            </Button>
          )}
          <Button variant="secondary" data-autofocus onClick={onClose}>
            {t("common.close")}
          </Button>
        </>
      }
    >
      {details.log && <pre className={styles.log}>{details.log}</pre>}
    </Dialog>
  );
}
