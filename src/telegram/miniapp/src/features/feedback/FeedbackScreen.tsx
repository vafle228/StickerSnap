import { useEffect, useId, useRef, useState } from "react";
import { Button, ConfirmDialog, Icon, Segmented, Switch } from "@stickersnap/ds";
import { ApiError, isUnauthorized } from "../../api/client";
import { useSendFeedback } from "../../api/queries";
import type { FeedbackType } from "../../api/types";
import { guardBack, useNav } from "../../app/nav";
import { Page, PageHead } from "../../shared/Layout";
import layout from "../../shared/layout.module.css";
import { useToast } from "../../shared/toast";
import { useTelegram } from "../../telegram/context";
import styles from "./feedback.module.css";

/** The service takes up to 2000 characters (contract) */
export const TEXT_MAX = 2000;

const TYPES: { value: FeedbackType; label: string }[] = [
  { value: "bug", label: "Ошибка" },
  { value: "idea", label: "Идея" },
  { value: "other", label: "Другое" },
];

/** The field's name follows the type, as in the desktop's «Написать отзыв» (19·4а) */
const FIELD: Record<FeedbackType, { label: string; hint?: string }> = {
  bug: { label: "Что случилось", hint: "Что делали и что увидели" },
  idea: { label: "Что предложить" },
  other: { label: "Что хотите сказать" },
};

/** Why it did not go, for the «Не отправилось» popup (18·8б) */
function failure(error: unknown): { text: string; retry: boolean } {
  if (error instanceof ApiError && error.status === 429) {
    return { text: "Слишком много отзывов подряд, попробуйте через час.", retry: false };
  }
  if (error instanceof ApiError && error.status === 0) {
    return { text: "Нет связи с сервисом. Текст остался в форме.", retry: true };
  }
  return { text: "Сервис не принял отзыв. Текст остался в форме.", retry: true };
}

/** «Отзыв» (artboard 18, 18·6): one form for a bug, an idea or anything else. It goes to the
 *  same POST /v1/feedback as the desktop's, and the service forwards it to the developer's chat.
 *  Opens from the card on «Мои паки» and from the bot's «Написать отзыв» and /feedback. */
export function FeedbackScreen() {
  const telegram = useTelegram();
  const send = useSendFeedback();
  const show = useToast((s) => s.show);
  const back = useNav((s) => s.back);
  const reset = useNav((s) => s.reset);
  const [type, setType] = useState<FeedbackType>("bug");
  const [text, setText] = useState("");
  const [canReply, setCanReply] = useState(true);
  const [empty, setEmpty] = useState(false);
  const [leaving, setLeaving] = useState(false);
  const area = useRef<HTMLTextAreaElement>(null);
  const areaId = useId();
  const hintId = `${areaId}-hint`;

  // Unsent text is not lost by accident: «Назад» asks first (18·6), and so does Telegram when
  // the Mini App is closed or swiped away
  const unsent = text.trim().length > 0;
  useEffect(() => {
    if (!unsent) return;
    const release = guardBack(() => setLeaving(true));
    telegram.setClosingConfirmation(true);
    return () => {
      release();
      telegram.setClosingConfirmation(false);
    };
  }, [unsent, telegram]);

  const submit = () => {
    const body = text.trim();
    // The button stays on: pressing it shows what is wrong (18·8)
    if (!body) {
      setEmpty(true);
      telegram.haptic("error");
      area.current?.focus();
      return;
    }
    send.mutate(
      { type, text: body, canReply, platform: telegram.platform },
      {
        onSuccess: () => {
          telegram.haptic("done");
          reset([{ kind: "feedbackSent" }]);
        },
        onError: (error) => {
          if (isUnauthorized(error)) return;
          const why = failure(error);
          telegram.haptic("error");
          show({
            tone: "danger",
            title: "Не отправилось",
            description: why.text,
            action: why.retry ? { label: "Повторить", run: submit } : undefined,
          });
        },
      },
    );
  };

  const field = FIELD[type];
  return (
    <Page label="Отзыв">
      <PageHead title="Отзыв" sub="Что сломалось или чего не хватает. Читаем каждый." />
      <Segmented<FeedbackType>
        label="Тип отзыва"
        className={styles.types}
        items={TYPES}
        value={type}
        onChange={setType}
      />
      <div className={styles.field}>
        <label className={`ss-field-label ${styles.label}`} htmlFor={areaId}>
          {field.label}
        </label>
        <textarea
          ref={area}
          id={areaId}
          className={`${styles.area} ${empty ? styles.invalid : ""}`}
          rows={5}
          maxLength={TEXT_MAX}
          value={text}
          aria-invalid={empty || undefined}
          aria-describedby={hintId}
          onChange={(e) => {
            setText(e.target.value);
            if (e.target.value.trim()) setEmpty(false);
          }}
        />
        {empty ? (
          <span className={styles.error} id={hintId} aria-live="polite">
            <Icon name="error-circle" size={14} strokeWidth={2} />
            Напишите хотя бы пару слов
          </span>
        ) : (
          <span className={styles.foot} id={hintId}>
            {field.hint && <span>{field.hint}</span>}
            <span className={styles.count}>
              {text.length} / {TEXT_MAX}
            </span>
          </span>
        )}
      </div>
      <Switch
        className={`${layout.solo} ${styles.switch}`}
        checked={canReply}
        onChange={setCanReply}
        label={
          <span className={layout.text}>
            <span className={layout.title}>Можно написать мне</span>
            <span className={layout.meta}>Ответим в Telegram, если нужно уточнить</span>
          </span>
        }
      />
      <div className={styles.spacer} />
      <Button variant="primary" className={styles.send} disabled={send.isPending} onClick={submit}>
        Отправить
      </Button>
      <ConfirmDialog
        open={leaving}
        title="Выйти без отправки?"
        text="Текст отзыва пропадёт."
        confirmLabel="Выйти"
        onConfirm={() => {
          setLeaving(false);
          back();
        }}
        onCancel={() => setLeaving(false)}
      />
    </Page>
  );
}
