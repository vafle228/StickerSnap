import { useQueryClient } from "@tanstack/react-query";
import { Icon, IconButton } from "@stickersnap/ds";
import { useJob } from "../../../shared/api/queries";
import type { SourceInfo } from "../../../shared/api/types";
import { t } from "../../../shared/strings/ru";
import { fps as fpsText, kilobytes, num } from "../format";
import { useSegment } from "../model/store";
import { cancelJob } from "../render/actions";
import { percent } from "../render/format";
import { useRender } from "../render/store";
import styles from "./VideoStage.module.css";

/** The glass chips at the bottom corners (artboard 3a): the source on the left, the kind of
 *  preview on the right. While a draft renders (3c, D) the right one is its progress with ×
 *  and the left one says the quick preview goes on meanwhile. */
export function StageChips({
  source,
  fps,
  draft,
  ready,
}: {
  source: SourceInfo;
  fps: number;
  /** «Точное превью» is on for the segment */
  draft: boolean;
  /** …and its draft of the segment as it is now has arrived */
  ready: boolean;
}) {
  const client = useQueryClient();
  const segment = useSegment();
  const preview = useRender((s) => s.preview);
  const bytes = useRender((s) => s.previewFile?.bytes ?? 0);
  const setExact = useRender((s) => s.setExact);
  const job = useJob(preview?.jobId || undefined).data;
  const rendering = draft && !ready;
  const loop = t(segment?.loop === "pingpong" ? "editor.loop.pingpong" : "editor.loop.normal");
  const value = percent(job?.progress.fraction ?? 0);

  const stop = () => {
    if (preview?.jobId) void cancelJob(client, preview.jobId);
    setExact(false);
  };

  return (
    <>
      <span
        className={`ss-card-glass ${styles.chip} ${styles.chipLeft} ${rendering ? styles.meanwhile : ""}`}
      >
        {rendering
          ? t("editor.preview.meanwhile")
          : t("editor.sourceInfo", {
              format: source.format.toUpperCase(),
              duration: num(source.durationSec, 1),
              fps: fpsText(fps),
              alpha: t(source.hasAlpha ? "editor.alpha.yes" : "editor.alpha.no"),
            })}
      </span>
      {rendering ? (
        <div
          className={`ss-card-glass ${styles.chip} ${styles.chipRight} ${styles.draft}`}
          role="status"
        >
          <span>{t("editor.preview.draft")}</span>
          <span
            className={`ss-progress-track ${styles.draftTrack}`}
            role="progressbar"
            aria-label={t("editor.preview.draft")}
            aria-valuemin={0}
            aria-valuemax={100}
            aria-valuenow={value}
          >
            <span className="ss-progress-fill" style={{ width: `${value}%` }} />
          </span>
          <span className={styles.draftValue}>{value}%</span>
          <IconButton
            icon="close"
            label={t("editor.preview.cancel")}
            size="sm"
            flat
            className={styles.round}
            onClick={stop}
          />
        </div>
      ) : (
        <span className={`ss-card-glass ${styles.chip} ${styles.chipRight}`}>
          <Icon name="repeat" size={16} />
          {ready
            ? t("editor.preview.exact", { loop, size: kilobytes(bytes) })
            : t("editor.preview.quick", { loop })}
        </span>
      )}
    </>
  );
}
