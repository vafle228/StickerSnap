import { useQueryClient } from "@tanstack/react-query";
import { useEffect } from "react";
import { Button, Card, Icon, Segmented, Switch } from "../../../ds";
import { useAppInfo, useEstimate, useJob } from "../../../shared/api/queries";
import type { Estimate, Job, Segment, SegmentPreset } from "../../../shared/api/types";
import { notifyProblem } from "../../../shared/notify";
import { t } from "../../../shared/strings/ru";
import { fps as fpsText, kilobytes, num, speed as speedText } from "../format";
import { editSegment, useEditor, useSegment } from "../model/store";
import { cancelJob, exportSegments } from "../render/actions";
import { percent, stageLine, stageTitle } from "../render/format";
import { useRender } from "../render/store";
import { useDebounced } from "../useDebounced";
import styles from "./Inspector.module.css";

const ESTIMATE_DELAY_MS = 400;

/** «Экспорт»: sticker or emoji, the size forecast and the long sticker (R7, R17, R19), then
 *  «Экспорт WEBM». While the segment is exported the card turns into its progress (3c, E);
 *  a forecast over the limit warns and offers a shorter sticker (3c, G). */
export function ExportCard() {
  const client = useQueryClient();
  const segment = useSegment();
  const segments = useEditor((s) => s.scene?.segments);
  const missing = useEditor((s) => s.source?.status === "missing");
  const exporting = useRender((s) => s.exporting);
  const job = useJob(exporting?.jobId).data;

  if (!segment || !segments) return null;
  if (job && exporting?.segmentIds.includes(segment.id)) {
    return (
      <ExportProgress job={job} segment={segment} onCancel={() => void cancelJob(client, job.id)} />
    );
  }

  // One export at a time; without the video there is nothing to render (artboard 6, 2).
  const reason = missing
    ? t("inspector.needSource")
    : exporting
      ? t("inspector.exportBusy")
      : undefined;
  return (
    <Card eyebrow={t("inspector.export")} className={styles.card}>
      <ExportSettings segment={segment} />
      <Button
        className={styles.wide}
        disabled={reason !== undefined}
        reason={reason}
        onClick={() => void exportSegments(client, [segment.id])}
      >
        {t("inspector.exportWebm")}
      </Button>
      {segments.length > 1 && (
        <button
          type="button"
          className={styles.link}
          disabled={reason !== undefined}
          onClick={() =>
            void exportSegments(
              client,
              segments.map((s) => s.id),
            )
          }
        >
          {t("inspector.exportAll", { count: segments.length })}
        </button>
      )}
    </Card>
  );
}

function ExportSettings({ segment }: { segment: Segment }) {
  const projectId = useEditor((s) => s.projectId);
  const limits = useAppInfo().data?.limits;
  const settled = useDebounced(segment, ESTIMATE_DELAY_MS);
  const estimate = useEstimate(projectId, settled);

  useEffect(() => {
    if (estimate.error) notifyProblem(estimate.error, { key: "estimate" });
  }, [estimate.error]);

  const forecast = estimate.data;
  return (
    <>
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
          <Forecast forecast={forecast} />
        ) : (
          <span className={styles.mono} role="status">
            {t("inspector.estimating")}
          </span>
        )}
      </div>

      {forecast && !forecast.fits && <OverLimit forecast={forecast} asIs={!segment.autoFit} />}

      {limits && (
        <Switch
          className={styles.long}
          checked={segment.autoFit}
          onChange={(on) => editSegment((s) => void (s.autoFit = on))}
          label={
            <span className={styles.longText}>
              <b>{t("inspector.autoFit", { size: kilobytes(limits[segment.preset].maxBytes) })}</b>
              <span className={segment.autoFit ? styles.quiet : styles.risky}>
                {segment.autoFit
                  ? t("inspector.autoFitOn")
                  : t("inspector.autoFitOff", {
                      size: kilobytes(limits[segment.preset].maxBytes),
                    })}
              </span>
            </span>
          }
        />
      )}

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
    </>
  );
}

/** «≈ 184 КБ / лимит 256 КБ» with its bar. Over the limit the number turns amber, the bar
 *  fills up and a mark shows where the limit is (3c, G). */
function Forecast({ forecast }: { forecast: Estimate }) {
  const over = !forecast.fits;
  const share = over
    ? (forecast.limitBytes / forecast.bytes) * 100
    : (forecast.bytes / forecast.limitBytes) * 100;
  const size = t("inspector.estimate", { size: kilobytes(forecast.bytes) });
  return (
    <>
      <div className={styles.forecastHead}>
        <b className={over ? styles.over : undefined}>{size}</b>
        <span>{t("inspector.limit", { size: kilobytes(forecast.limitBytes) })}</span>
      </div>
      <div className={styles.meter}>
        <div
          className="ss-progress-track"
          role="meter"
          aria-label={size}
          aria-valuemin={0}
          aria-valuemax={forecast.limitBytes}
          aria-valuenow={forecast.bytes}
        >
          <div
            className={`ss-progress-fill ${over ? styles.overFill : ""}`}
            style={{ width: over ? "100%" : `${share}%` }}
          />
        </div>
        {over && <span className={styles.limitMark} style={{ left: `${share}%` }} aria-hidden />}
      </div>
      <span className={styles.mono}>
        {t("inspector.estimateLine", {
          fps: fpsText(forecast.output.fps),
          duration: num(forecast.output.durationSec, 2),
        })}
      </span>
    </>
  );
}

/** 3c, G: a warning, not a ban. The export will walk the ladder itself; the buttons make the
 *  sticker shorter in one click instead, as an ordinary edit. With the fitting switched off
 *  (`asIs`) nothing is given up, and the file comes out over the limit as the user chose. */
function OverLimit({ forecast, asIs }: { forecast: Estimate; asIs: boolean }) {
  const likely = forecast.likelyConcessions[0];
  const limit = kilobytes(forecast.limitBytes);
  const fixes = forecast.suggestions.length > 0;
  const title = asIs
    ? t("inspector.asIs.title", { size: kilobytes(forecast.bytes) })
    : likely
      ? t(`inspector.likely.${likely.kind}`, { to: num(likely.to) })
      : t("inspector.likely.none");
  const text = asIs
    ? t(fixes ? "inspector.asIs.text" : "inspector.asIs.textNoFix", { limit })
    : fixes
      ? t("inspector.keepQuality")
      : undefined;
  return (
    <div className={`ss-tone-warning ${styles.warn}`} role="status">
      <span className="ss-tone-icon">
        <Icon name="alert" size={18} strokeWidth={1.75} />
      </span>
      <div className={styles.warnBody}>
        <p className={styles.warnTitle}>{title}</p>
        {text && <p className={styles.warnText}>{text}</p>}
        {fixes && (
          <>
            <div className={styles.warnActions}>
              {forecast.suggestions.map((s) =>
                s.kind === "trim" ? (
                  <Button
                    key="trim"
                    size="sm"
                    onClick={() => editSegment((seg) => void (seg.outFrame = s.outFrame))}
                  >
                    {t("editor.over.shorten", { duration: num(s.durationSec, 1) })}
                  </Button>
                ) : (
                  <Button
                    key="speed"
                    size="sm"
                    onClick={() => editSegment((seg) => void (seg.speed = s.speed))}
                  >
                    {t("editor.over.speedUp", { speed: speedText(s.speed) })}
                  </Button>
                ),
              )}
            </div>
          </>
        )}
      </div>
    </div>
  );
}

/** 3c, E: «Экспорт WEBM» turned into its progress. The rest of the editor stays live; what is
 *  edited now does not reach this file, the export goes by the saved revision. */
function ExportProgress({
  job,
  segment,
  onCancel,
}: {
  job: Job;
  segment: Segment;
  onCancel: () => void;
}) {
  const item = job.items.find((i) => i.segmentId === segment.id);
  const value = percent(item?.fraction ?? 0);
  return (
    <Card eyebrow={t("inspector.export")} className={styles.card}>
      <div className={styles.progress}>
        <div className={styles.progressHead}>
          <b>{stageTitle(job, item)}</b>
          <span>{value}%</span>
        </div>
        <div
          className="ss-progress-track"
          role="progressbar"
          aria-label={t("inspector.export")}
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={value}
        >
          <div className="ss-progress-fill" style={{ width: `${value}%` }} />
        </div>
        <span className={styles.mono}>{stageLine(job, item)}</span>
      </div>
      <p className={styles.note}>
        <Icon name="info" size={18} />
        <span>{t("export.snapshot")}</span>
      </p>
      <Button className={styles.wide} onClick={onCancel}>
        {t("export.cancel")}
      </Button>
    </Card>
  );
}
