import { useEffect } from "react";
import { Button, Card, Segmented, Switch } from "../../../ds";
import { useAppInfo, useEstimate } from "../../../shared/api/queries";
import type { SegmentPreset } from "../../../shared/api/types";
import { notifyProblem } from "../../../shared/notify";
import { t } from "../../../shared/strings/ru";
import { fps as fpsText, kilobytes, num } from "../format";
import { editSegment, useEditor, useSegment } from "../model/store";
import { useDebounced } from "../useDebounced";
import styles from "./Inspector.module.css";

const ESTIMATE_DELAY_MS = 400;

/** «Экспорт»: sticker or emoji, the size forecast and the long sticker (R7, R17, R19).
 *  The export itself, its progress and the «не влезет» advice (3c E–H) are the export task;
 *  the WEBM button waits for it. */
export function ExportCard() {
  const segment = useSegment();
  const projectId = useEditor((s) => s.projectId);
  const segmentCount = useEditor((s) => s.scene?.segments.length ?? 0);
  const limits = useAppInfo().data?.limits;
  const settled = useDebounced(segment, ESTIMATE_DELAY_MS);
  const estimate = useEstimate(projectId, settled);

  useEffect(() => {
    if (estimate.error) notifyProblem(estimate.error, { key: "estimate" });
  }, [estimate.error]);

  if (!segment) return null;
  const forecast = estimate.data;
  const share = forecast ? Math.min(100, (forecast.bytes / forecast.limitBytes) * 100) : 0;

  return (
    <Card eyebrow={t("inspector.export")} className={styles.card}>
      <Segmented<SegmentPreset>
        label={t("inspector.exportType")}
        className={styles.wide}
        items={[
          {
            value: "sticker",
            label: t("inspector.type.sticker", { side: limits?.sticker.side ?? "" }),
          },
          { value: "emoji", label: t("inspector.type.emoji", { side: limits?.emoji.side ?? "" }) },
        ]}
        value={segment.preset}
        onChange={(preset) => editSegment((s) => void (s.preset = preset))}
      />

      <div className={styles.forecast} aria-busy={estimate.isFetching}>
        {forecast ? (
          <>
            <div className={styles.forecastHead}>
              <b>{t("inspector.estimate", { size: kilobytes(forecast.bytes) })}</b>
              <span>{t("inspector.limit", { size: kilobytes(forecast.limitBytes) })}</span>
            </div>
            <div
              className="ss-progress-track"
              role="meter"
              aria-label={t("inspector.estimate", { size: kilobytes(forecast.bytes) })}
              aria-valuemin={0}
              aria-valuemax={forecast.limitBytes}
              aria-valuenow={forecast.bytes}
            >
              <div className="ss-progress-fill" style={{ width: `${share}%` }} />
            </div>
            <span className={styles.mono}>
              {t("inspector.estimateLine", {
                fps: fpsText(forecast.output.fps),
                duration: num(forecast.output.durationSec, 2),
              })}
            </span>
          </>
        ) : (
          <span className={styles.mono} role="status">
            {t("inspector.estimating")}
          </span>
        )}
      </div>

      {limits && (
        <Switch
          className={styles.long}
          checked={segment.longSticker}
          onChange={(on) => editSegment((s) => void (s.longSticker = on))}
          label={
            <span className={styles.longText}>
              <b>{t("inspector.longSticker")}</b>
              <span>
                {t("inspector.longStickerHint", {
                  max: num(limits[segment.preset].maxDurationSec),
                })}
              </span>
            </span>
          }
        />
      )}

      <Button className={styles.wide} disabled>
        {t("inspector.exportWebm")}
      </Button>
      {segmentCount > 1 && (
        <button type="button" className={styles.link} disabled>
          {t("inspector.exportAll", { count: segmentCount })}
        </button>
      )}
    </Card>
  );
}
