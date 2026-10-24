import type { ReactNode } from "react";
import { Button, Icon, Progress } from "@stickersnap/ds";
import { cx } from "@stickersnap/ds/cx";
import type { Download } from "../../../shared/api/types";
import { t } from "../../../shared/strings/ru";
import { Spinner } from "../Spinner";
import { amount, downloadMeta, eta, isLong, longNote, percentOf } from "./format";
import styles from "./Link.module.css";
import { useLinkActions, useLinkImport } from "./linkImport";
import { shortLink } from "./parse";
import { useShownDownload } from "./shown";

/** The card in the import zone's place (artboard 20, 3·4–3·6): what the video is, the rainbow
 *  bar with megabytes and time left, then «Читаю» as with a file. «Отменить» on every step. */
export function LinkProgress({ className }: { className?: string }) {
  const probing = useLinkImport((s) => s.probing);
  const download = useShownDownload();
  const { cancel } = useLinkActions();
  if (!download && !probing?.slow) return null;

  const cancelButton = (
    <Button size="sm" variant="ghost" onClick={() => void cancel()}>
      {t("import.cancel")}
    </Button>
  );

  return (
    <div className={cx(styles.place, className)}>
      <div className={styles.card} role="status">
        {!download ? (
          <div className={styles.cardRow}>
            <Spinner size={32} />
            <div className={styles.cardText}>
              <b>{t("link.probing")}</b>
              <span className={styles.mono}>{shortLink(probing?.url ?? "")}</span>
            </div>
            {cancelButton}
          </div>
        ) : download.state === "reading" ? (
          <div className={styles.cardRow}>
            <Spinner size={32} />
            <div className={styles.cardText}>
              <b>{t("import.reading", { name: download.video.title })}</b>
              <span>{t("import.readingWhat")}</span>
            </div>
            {cancelButton}
          </div>
        ) : (
          <Downloading download={download} cancel={cancelButton} />
        )}
      </div>
    </div>
  );
}

/** 3·5: the preview, title and length, then the bar; a long video gets its yellow line. */
function Downloading({ download, cancel }: { download: Download; cancel: ReactNode }) {
  const { video } = download;
  const percent = percentOf(download);
  return (
    <>
      <div className={styles.cardRow}>
        <span className={cx(styles.thumb, video.site === "giphy" && styles.square)}>
          {video.thumbnailUrl ? (
            <img src={video.thumbnailUrl} alt="" />
          ) : (
            <Icon name="film" size={22} />
          )}
        </span>
        <div className={styles.cardText}>
          <b title={video.title}>{video.title}</b>
          <span>{downloadMeta(download)}</span>
        </div>
        {cancel}
      </div>
      <div className={styles.bar}>
        <Progress value={percent} variant="rainbow" showValue={false} />
        <div className={styles.numbers}>
          <span>{amount(download)}</span>
          <span>
            {t("link.percent", { percent })}
            {download.state === "downloading" &&
              download.etaSec !== undefined &&
              ` · ${eta(download.etaSec)}`}
          </span>
        </div>
      </div>
      {isLong(video) && (
        <p className={styles.long}>
          <Icon name="alert" size={16} strokeWidth={2} />
          <span>{longNote(video)}</span>
        </p>
      )}
    </>
  );
}
