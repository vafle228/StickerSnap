import { Badge, Button, Dialog, Icon, type Tone } from "../../ds";
import type { ValidationReport } from "../../shared/api/types";
import { plural, t } from "../../shared/strings/ru";
import { kilobytes } from "../editor/format";
import { checkValue, failedCount, RULE_LABEL } from "./format";
import styles from "./ValidationDialog.module.css";

type Status = ValidationReport["checks"][number]["status"];

const STATUS: Record<Status, { tone: Tone; icon: "check-circle" | "error-circle" | "alert" }> = {
  pass: { tone: "success", icon: "check-circle" },
  fail: { tone: "danger", icon: "error-circle" },
  warn: { tone: "warning", icon: "alert" },
};

const VERDICT: Record<ValidationReport["verdict"], Tone> = {
  pass: "success",
  fail: "danger",
  experimental: "warning",
};

/** The check of a .webm (R8): every rule of Telegram's table with what the file has, the rule's
 *  bound under a broken one. Read-only, so a click beside it closes it (artboard 12). A long
 *  sticker (R19) is «экспериментальный», not an error. */
export function ValidationDialog({
  report,
  onClose,
  onMakeSticker,
}: {
  report: ValidationReport;
  onClose: () => void;
  onMakeSticker?: () => void;
}) {
  const failed = failedCount(report);
  const verdict =
    report.verdict === "fail"
      ? t(`validator.verdict.fail.${plural(failed)}`, { n: failed })
      : t(`validator.verdict.${report.verdict}`);

  return (
    <Dialog
      title={t("validator.title", { name: report.fileName })}
      dismissible
      onClose={onClose}
      actions={
        <>
          {onMakeSticker && (
            <Button variant="ghost" onClick={onMakeSticker}>
              {t("validator.makeSticker")}
            </Button>
          )}
          <Button data-autofocus onClick={onClose}>
            {t("common.close")}
          </Button>
        </>
      }
    >
      <div className={styles.summary}>
        <Badge tone={VERDICT[report.verdict]} icon={STATUS[toStatus(report.verdict)].icon}>
          {t(`validator.badge.${report.verdict}`)}
        </Badge>
        <span className={styles.bytes}>{kilobytes(report.bytes)}</span>
      </div>
      <p className={styles.verdict}>{verdict}</p>
      <ul className={styles.list} aria-label={t("validator.rules")}>
        {report.checks.map((check) => {
          const { tone, icon } = STATUS[check.status];
          return (
            <li key={check.rule} className={`ss-row ss-tone-${tone} ${styles.row}`}>
              <span className={`ss-tone-icon ${styles.icon}`}>
                <Icon name={icon} size={16} strokeWidth={2} />
                <span className="ss-visually-hidden">{t(`validator.status.${check.status}`)}</span>
              </span>
              <span className={styles.rule}>{t(RULE_LABEL[check.rule])}</span>
              <span className={styles.values}>
                <span className={styles.actual}>{checkValue(check.rule, check.actual)}</span>
                {check.status !== "pass" && (
                  <span className={styles.expected}>
                    {t("validator.expected", { value: checkValue(check.rule, check.expected) })}
                  </span>
                )}
              </span>
            </li>
          );
        })}
      </ul>
    </Dialog>
  );
}

function toStatus(verdict: ValidationReport["verdict"]): Status {
  return verdict === "pass" ? "pass" : verdict === "fail" ? "fail" : "warn";
}
