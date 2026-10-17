import { Button } from "@stickersnap/ds";
import { cx } from "@stickersnap/ds/cx";
import { t } from "../../shared/strings/ru";
import styles from "./ReadingFile.module.css";
import { cancelReading, useImport } from "./reading";

/** Artboard 3c, A: the file is read, «Отменить» drops the request. Only after 300 ms, so a
 *  quick read never blinks; until then the zone just takes no more files. No percent: reading
 *  usually takes a fraction of a second. */
export function ReadingFile({ className }: { className?: string }) {
  const reading = useImport((s) => s.reading);
  if (!reading?.slow) return null;
  const webm = reading.kind === "webm";

  return (
    <div className={cx(styles.reading, className)} role="status">
      <svg className={styles.spinner} width="44" height="44" viewBox="0 0 44 44" aria-hidden>
        <circle className={styles.track} cx="22" cy="22" r="18" />
        <path className={styles.arc} d="M22 4a18 18 0 0 1 18 18" />
      </svg>
      <div className={styles.text}>
        <span className={styles.title}>
          {t(webm ? "import.checking" : "import.reading", { name: reading.name })}
        </span>
        <span className={styles.what}>
          {t(webm ? "import.checkingWhat" : "import.readingWhat")}
        </span>
      </div>
      <Button size="sm" variant="ghost" onClick={cancelReading}>
        {t("import.cancel")}
      </Button>
    </div>
  );
}
