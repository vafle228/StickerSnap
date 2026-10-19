import { useId, useRef, useState } from "react";
import { Banner, Button, Checkbox, Dialog, Icon, Segmented } from "@stickersnap/ds";
import { useAccount, useSendFeedback } from "../../shared/api/queries";
import type { FeedbackType } from "../../shared/api/types";
import { confirm, notify, toProblem } from "../../shared/notify";
import { t } from "../../shared/strings/ru";
import styles from "./feedback.module.css";

/** The service takes up to 2000 characters of text and 100 of contact (contract) */
export const TEXT_MAX = 2000;
export const CONTACT_MAX = 100;

const TYPES: FeedbackType[] = ["bug", "idea", "other"];

/** Why it did not go: the strip under the buttons, the text stays in the window (19·4) */
interface Failure {
  title: string;
  text: string;
  retry: boolean;
}

function failureOf(error: unknown): Failure {
  const problem = toProblem(error);
  switch (problem.code) {
    case "network_unavailable":
      return { title: t("feedback.offline"), text: t("feedback.offlineText"), retry: true };
    case "bot_unavailable":
      return { title: t("feedback.unavailable"), text: t("feedback.unavailableText"), retry: true };
    case "rate_limited": {
      const seconds = Number(problem.params?.retryAfterSec ?? 3600);
      const minutes = Math.max(1, Math.ceil(seconds / 60));
      return {
        title: t("feedback.limit"),
        text: t("feedback.limitText", { minutes }),
        retry: false,
      };
    }
    default:
      return { title: t("feedback.failed"), text: t("feedback.failedText"), retry: true };
  }
}

/** «Написать отзыв» (artboard 19·4): one window for a bug, an idea or anything else. A bug goes
 *  with the server's report for the developer, always and without a checkbox. A linked computer
 *  is answered by the bot (19·4а); one that is not gives an e-mail or @username in one row (19·4в). */
export function FeedbackDialog({ onClose }: { onClose: () => void }) {
  const linked = useAccount().data?.state === "linked";
  const send = useSendFeedback();
  const [type, setType] = useState<FeedbackType>("idea");
  const [text, setText] = useState("");
  const [canReply, setCanReply] = useState(true);
  const [contact, setContact] = useState("");
  const [empty, setEmpty] = useState(false);
  const [showReport, setShowReport] = useState(false);
  const [failure, setFailure] = useState<Failure>();
  const area = useRef<HTMLTextAreaElement>(null);
  const areaId = useId();
  const hintId = `${areaId}-hint`;

  // «Отмена», the cross and Esc: written text is not dropped without asking (19·4)
  const leave = async () => {
    if (send.isPending) return;
    if (text.trim()) {
      const sure = await confirm({
        title: t("feedback.leave"),
        text: t("feedback.leaveText"),
        confirmLabel: t("feedback.leaveConfirm"),
        cancelLabel: t("feedback.leaveCancel"),
      });
      if (!sure) return;
    }
    onClose();
  };

  const submit = () => {
    const body = text.trim();
    // The button stays on: pressing it shows what is missing
    if (!body) {
      setEmpty(true);
      area.current?.focus();
      return;
    }
    const address = contact.trim();
    // Without a link an answer needs somewhere to go: no contact, no promise to answer
    const reply = canReply && (linked || address.length > 0);
    setFailure(undefined);
    send.mutate(
      {
        type,
        text: body,
        canReply: reply,
        ...(reply && !linked ? { contact: address } : {}),
      },
      {
        onSuccess: () => {
          onClose();
          notify.toast({
            tone: "success",
            title: t("feedback.sent"),
            description: !reply
              ? t("feedback.sentQuiet")
              : linked
                ? t("feedback.sentText")
                : t("feedback.sentContact", { contact: address }),
            timeout: 6000,
            key: "feedback",
          });
        },
        onError: (error) => setFailure(failureOf(error)),
      },
    );
  };

  return (
    <Dialog
      className={styles.window}
      title={t("feedback.title")}
      text={type === "bug" ? undefined : t("feedback.text")}
      onClose={() => void leave()}
    >
      <div className={styles.body}>
        <Segmented<FeedbackType>
          label={t("feedback.types")}
          className={styles.types}
          items={TYPES.map((value) => ({ value, label: t(`feedback.type.${value}`) }))}
          value={type}
          onChange={setType}
        />
        <div className={styles.field}>
          <label className={`ss-field-label ${styles.label}`} htmlFor={areaId}>
            {t(`feedback.label.${type}`)}
          </label>
          <textarea
            ref={area}
            id={areaId}
            className={`${styles.area} ${empty ? styles.invalid : ""}`}
            rows={5}
            maxLength={TEXT_MAX}
            value={text}
            placeholder={t(`feedback.placeholder.${type}`)}
            aria-invalid={empty || undefined}
            aria-describedby={hintId}
            data-autofocus
            onChange={(e) => {
              setText(e.target.value);
              if (e.target.value.trim()) setEmpty(false);
            }}
          />
          {empty ? (
            <span className={styles.error} id={hintId} aria-live="polite">
              <Icon name="error-circle" size={14} strokeWidth={2} />
              {t("feedback.empty")}
            </span>
          ) : (
            <span className={styles.hint} id={hintId}>
              {t("feedback.count", { count: text.length, max: TEXT_MAX })}
            </span>
          )}
        </div>

        {type === "bug" && (
          <div className={styles.report}>
            <Icon name="info" size={18} />
            <div className={styles.reportBody}>
              <span>
                {t("feedback.report")}{" "}
                <button
                  type="button"
                  className={styles.link}
                  aria-expanded={showReport}
                  onClick={() => setShowReport((open) => !open)}
                >
                  {t("feedback.reportWhat")}
                </button>
              </span>
              {showReport && (
                <>
                  <ul className={styles.list}>
                    <li>{t("feedback.report.versions")}</li>
                    <li>{t("feedback.report.mode")}</li>
                    <li>{t("feedback.report.time")}</li>
                  </ul>
                  <span className={styles.muted}>{t("feedback.reportHint")}</span>
                </>
              )}
            </div>
          </div>
        )}

        {linked ? (
          <Checkbox
            className={styles.reply}
            checked={canReply}
            onChange={setCanReply}
            label={
              <span className={styles.replyText}>
                {t("feedback.canReply")}
                <small>{t("feedback.canReplyHint")}</small>
              </span>
            }
          />
        ) : (
          <div className={styles.replyRow}>
            <Checkbox checked={canReply} onChange={setCanReply} label={t("feedback.replyTo")} />
            <input
              className={styles.contact}
              aria-label={t("feedback.contact")}
              placeholder={t("feedback.contactPlaceholder")}
              maxLength={CONTACT_MAX}
              disabled={!canReply}
              value={contact}
              onChange={(e) => setContact(e.target.value)}
            />
          </div>
        )}

        {/* The buttons sit in the body: a failure shows under them, the text stays above (19·4) */}
        <div className="ss-dialog-actions">
          <Button onClick={() => void leave()}>{t("common.cancel")}</Button>
          <Button variant="primary" disabled={send.isPending} onClick={submit}>
            {t("feedback.send")}
          </Button>
        </div>

        {failure && (
          <Banner
            className={styles.failure}
            tone={failure.retry ? "danger" : "warning"}
            title={failure.title}
            description={failure.text}
            action={
              failure.retry ? (
                <Button size="sm" disabled={send.isPending} onClick={submit}>
                  {t("common.retry")}
                </Button>
              ) : undefined
            }
          />
        )}
      </div>
    </Dialog>
  );
}
