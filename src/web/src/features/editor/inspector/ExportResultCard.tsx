import { Badge, Button, Card, Icon } from "../../../ds";
import { cx } from "../../../ds/cx";
import { useAppInfo } from "../../../shared/api/queries";
import { platform } from "../../../shared/platform";
import { t } from "../../../shared/strings/ru";
import { showReport } from "../../validator/check";
import { kilobytes, num } from "../format";
import { useEditor, useSegment } from "../model/store";
import { ladderText, lostOf, sideUntouched } from "../render/format";
import { useFreshResult, useRender } from "../render/store";
import { useFileUrl } from "../render/useFileUrl";
import styles from "./Inspector.module.css";

const VALID = {
  pass: { tone: "success", icon: "check" },
  fail: { tone: "danger", icon: "error-circle" },
  experimental: { tone: "warning", icon: "alert" },
} as const;

/** 3c, H: the segment's last export while it is not edited again. What came out, whether
 *  Telegram takes it, and, if quality was given up for 256 КБ, what and how (R7). */
export function ExportResultCard() {
  const segment = useSegment();
  const result = useFreshResult(segment);
  const compare = useRender((s) => s.compare);
  const setCompare = useRender((s) => s.setCompare);
  const desktop = useAppInfo().data?.mode === "desktop";
  const limits = useAppInfo().data?.limits;
  const video = useFileUrl(result?.fileUrl);
  if (!segment || !result) return null;

  const given = result.concessions.length > 0;
  const valid = VALID[result.validation.verdict];
  const limit = limits?.[segment.preset].maxBytes ?? 262144;
  const side = Math.max(result.output.width, result.output.height);

  return (
    <Card className={styles.card} aria-label={t("result.title", { file: `${segment.name}.webm` })}>
      <div className={styles.resultHead}>
        <div className={`ss-checker ${styles.thumb}`}>
          {video && <video src={video} autoPlay loop muted playsInline />}
        </div>
        <div className={styles.resultText}>
          <p className={styles.resultTitle}>
            {t("result.title", { file: `${segment.name}.webm` })}
            {given && (
              <Badge tone="warning" className={styles.resultBadge}>
                {t("export.concessions")}
              </Badge>
            )}
          </p>
          <span className={styles.mono}>
            {t("result.size", {
              size: kilobytes(result.bytes),
              limit: num(Math.round(limit / 1024)),
            })}
            <br />
            {t("result.specs", {
              width: result.output.width,
              height: result.output.height,
              fps: num(result.output.fps),
              duration: num(result.output.durationSec, 2),
            })}
          </span>
          <button
            type="button"
            className={cx(styles.valid, `ss-tone-${valid.tone}`)}
            aria-haspopup="dialog"
            onClick={() => showReport(result.validation)}
          >
            <Icon name={valid.icon} size={16} strokeWidth={2} />
            {t(`result.valid.${result.validation.verdict}`)}
          </button>
        </div>
      </div>

      <div className={styles.resultActions}>
        <Button
          aria-pressed={compare}
          onClick={() => {
            setCompare(!compare);
            // Side by side they are watched in motion, in step (3c, H).
            if (!compare) useEditor.getState().setPlaying(true);
          }}
        >
          {t(compare ? "editor.compare.done" : "result.compare")}
        </Button>
        {desktop && result.path && (
          <Button
            variant="ghost"
            className={styles.reveal}
            onClick={() => platform.showInFolder(result.path ?? "")}
          >
            {t("result.reveal")}
          </Button>
        )}
      </div>

      {given && (
        <section className={styles.section}>
          <h4 className={`ss-card-eyebrow ${styles.sectionTitle}`}>{t("result.lost")}</h4>
          <dl className={styles.lost}>
            {lostOf(result).map((lost) => (
              <div key={lost.label} className={cx(styles.lostRow, lost.given && styles.given)}>
                <dt>{lost.label}</dt>
                <dd>{lost.value}</dd>
              </div>
            ))}
          </dl>
        </section>
      )}

      {result.ladder.length > 1 && (
        <section className={styles.section}>
          <h4 className={`ss-card-eyebrow ${styles.sectionTitle}`}>{t("result.ladder")}</h4>
          <ol className={styles.ladder}>
            {result.ladder.map((step, i) => {
              const fits = i === result.ladder.length - 1;
              return (
                <li key={i} className={cx(styles.rung, fits && styles.fits)}>
                  <Icon name={fits ? "check" : "close"} size={14} strokeWidth={2.25} />
                  <span className="ss-visually-hidden">
                    {t(fits ? "result.fits" : "result.tooBig")}
                  </span>
                  {ladderText(step, result)}
                </li>
              );
            })}
            {sideUntouched(result) && (
              <li className={cx(styles.rung, styles.skipped)}>
                {t("result.ladderSkipped", { side: num(side) })}
              </li>
            )}
          </ol>
        </section>
      )}
    </Card>
  );
}
