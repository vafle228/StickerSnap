import { useEffect, useRef, useState, type ReactNode } from "react";
import { Button, Icon, Switch, type IconName } from "@stickersnap/ds";
import { ApiError, isUnauthorized } from "../../api/client";
import { useConfirmLink, useDeclineLink, useLinkRequest } from "../../api/queries";
import type { LinkRequest } from "../../api/types";
import { useNav } from "../../app/nav";
import { Notice, Page } from "../../shared/Layout";
import layout from "../../shared/layout.module.css";
import { useToast } from "../../shared/toast";
import { useDelayed } from "../../shared/useDelayed";
import { useTelegram } from "../../telegram/context";
import { requestedLabel, systemLabel } from "./format";
import styles from "./link.module.css";

type Outcome = { kind: "linked"; name: string } | { kind: "expired" } | { kind: "declined" };

const expired = (error: unknown) => error instanceof ApiError && error.status === 404;

/** «Привязать этот компьютер?» (artboard 18, 18·1): the one place where a computer's link is
 *  confirmed. It opens only from the desktop's link (startapp=link_<code>) and ends in one of
 *  18·2–18·4. */
export function LinkScreen({ code }: { code: string }) {
  const request = useLinkRequest(code);
  const [outcome, setOutcome] = useState<Outcome | null>(null);
  const slow = useDelayed(request.isPending);

  if (outcome) return <LinkOutcome outcome={outcome} />;
  if (request.isError) {
    return expired(request.error) ? (
      <LinkOutcome outcome={{ kind: "expired" }} />
    ) : (
      <Page label="Привязка компьютера">
        <Notice
          tone="danger"
          title="Не удалось загрузить запрос"
          text="Нет связи с сервером StickerSnap."
          action={
            <Button size="sm" onClick={() => void request.refetch()}>
              Повторить
            </Button>
          }
        />
      </Page>
    );
  }
  if (!request.isSuccess) {
    return <Page label="Привязка компьютера">{slow && <LinkSkeleton />}</Page>;
  }
  return request.data.linkedHere ? (
    <AlreadyLinked link={request.data} onDone={setOutcome} />
  ) : (
    <LinkQuestion link={request.data} onDone={setOutcome} />
  );
}

function LinkQuestion({ link, onDone }: { link: LinkRequest; onDone: (o: Outcome) => void }) {
  const telegram = useTelegram();
  const confirm = useConfirmLink(link.code);
  const decline = useDeclineLink(link.code);
  const show = useToast((s) => s.show);
  const [notifications, setNotifications] = useState(true);
  // «Разрешить» and «Повторить» in a popup send the switch as it is then, not as it was
  const switched = useRef(notifications);
  switched.current = notifications;
  const [asking, setAsking] = useState(false);
  const busy = asking || confirm.isPending || decline.isPending;

  const failed = (error: unknown, title: string, retry: () => void) => {
    if (isUnauthorized(error)) return;
    if (expired(error)) {
      onDone({ kind: "expired" });
      return;
    }
    telegram.haptic("error");
    show({
      tone: "danger",
      title,
      description: "Нет связи с сервером StickerSnap.",
      action: { label: "Повторить", run: retry },
    });
  };

  // Decided 18 October: the bot must be allowed to write to the user before it links, since
  // Telegram likely refuses to make a pack for someone who never let the bot write (18·1б).
  const allowed = async () => {
    if (telegram.canWrite()) return true;
    setAsking(true);
    const granted = await telegram.askWriteAccess();
    setAsking(false);
    if (!granted) {
      telegram.haptic("error");
      show({
        tone: "warning",
        title: "Компьютер не привязан",
        description: "Без разрешения писать вам бот не сможет создавать паки от вашего имени.",
        action: { label: "Разрешить", run: () => void accept() },
      });
    }
    return granted;
  };

  const accept = async () => {
    if (!(await allowed())) return;
    confirm.mutate(switched.current, {
      onSuccess: () => {
        telegram.haptic("done");
        onDone({ kind: "linked", name: link.name });
      },
      onError: (error) => failed(error, "Компьютер не привязан", () => void accept()),
    });
  };

  const refuse = () => {
    decline.mutate(undefined, {
      onSuccess: () => onDone({ kind: "declined" }),
      onError: (error) => failed(error, "Привязка не отменена", refuse),
    });
  };

  return (
    <Page label="Привязка компьютера">
      <div className={styles.head}>
        <span className={layout.emptyIcon}>
          <Icon name="computer" size={34} />
        </span>
        <h1 className={styles.title}>Привязать этот компьютер?</h1>
        <p className={layout.sub}>
          Он сможет создавать паки и публиковать в них стикеры от вашего имени.
        </p>
      </div>
      <div className={styles.device}>
        <span className={`${layout.deviceIcon} ${styles.deviceIcon}`}>
          <Icon name="computer" size={22} />
        </span>
        <dl className={styles.facts} aria-label="Запрос на привязку">
          <dt>Компьютер</dt>
          <dd>
            <b>{link.name}</b>
          </dd>
          <dt>Система</dt>
          <dd>{systemLabel(link)}</dd>
          {link.appVersion && (
            <>
              <dt>Приложение</dt>
              <dd>StickerSnap {link.appVersion}</dd>
            </>
          )}
          <dt>Запрос</dt>
          <dd>{requestedLabel(link.requestedAt)}</dd>
        </dl>
      </div>
      <Switch
        className={`${layout.solo} ${styles.switch}`}
        checked={notifications}
        onChange={setNotifications}
        label={
          <span className={layout.text}>
            <span className={layout.title}>Уведомления в чат</span>
            <span className={layout.meta}>Бот напишет, когда стикер опубликован</span>
          </span>
        }
      />
      <div className={styles.spacer} />
      <p className={`${layout.foot} ${styles.center}`}>
        Привязывайте только свой компьютер. Если ссылку прислал кто-то другой, нажмите «Это не я».
      </p>
      <div className={styles.actions}>
        <Button variant="primary" disabled={busy} onClick={() => void accept()}>
          Привязать
        </Button>
        <Button variant="ghost" disabled={busy} onClick={refuse}>
          Это не я
        </Button>
      </div>
    </Page>
  );
}

/** The same computer of the same person: 18·2 without asking (artboard 18, «Уже привязан»).
 *  The service still has to hear it, or the desktop keeps waiting. */
function AlreadyLinked({ link, onDone }: { link: LinkRequest; onDone: (o: Outcome) => void }) {
  const telegram = useTelegram();
  const confirm = useConfirmLink(link.code);
  const sent = useRef(false);
  const slow = useDelayed(!confirm.isError);

  const send = () =>
    confirm.mutate(true, {
      onSuccess: () => {
        telegram.haptic("done");
        onDone({ kind: "linked", name: link.name });
      },
      onError: (error) => {
        if (expired(error)) onDone({ kind: "expired" });
        else if (!isUnauthorized(error)) telegram.haptic("error");
      },
    });

  useEffect(() => {
    // Strict Mode mounts twice; the link is confirmed once
    if (sent.current) return;
    sent.current = true;
    send();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <Page label="Привязка компьютера">
      {confirm.isError ? (
        <Notice
          tone="danger"
          title="Компьютер не привязан"
          text="Нет связи с сервером StickerSnap."
          action={
            <Button size="sm" onClick={send}>
              Повторить
            </Button>
          }
        />
      ) : (
        slow && <LinkSkeleton />
      )}
    </Page>
  );
}

const OUTCOMES: Record<
  Outcome["kind"],
  { icon: IconName; tone: string; title: string; text: (o: Outcome) => ReactNode }
> = {
  linked: {
    icon: "check",
    tone: styles.done ?? "",
    title: "Компьютер привязан",
    text: (o) =>
      `Вернитесь в StickerSnap на ${o.kind === "linked" ? o.name : "компьютере"}: он уже знает и может публиковать.`,
  },
  expired: {
    icon: "alert",
    tone: styles.warning ?? "",
    title: "Ссылка устарела",
    text: () =>
      "Ссылка живёт 10 минут и открывается один раз. Нажмите «Привязать» в StickerSnap ещё раз.",
  },
  declined: {
    icon: "info",
    tone: styles.info ?? "",
    title: "Привязка отменена",
    text: () => "Компьютер не получил доступа к вашим пакам.",
  },
};

/** 18·2–18·4: the whole screen tells how it ended, and leads on to «Мои паки» */
function LinkOutcome({ outcome }: { outcome: Outcome }) {
  const home = useNav((s) => s.home);
  const look = OUTCOMES[outcome.kind];
  return (
    <Page label="Привязка компьютера">
      <div className={layout.empty} role="status">
        <span className={`${styles.big} ${look.tone}`}>
          <Icon name={look.icon} size={34} strokeWidth={2} />
        </span>
        <h1 className={layout.emptyTitle}>{look.title}</h1>
        <p className={layout.emptyText}>{look.text(outcome)}</p>
        <div className={layout.emptyAction}>
          <Button onClick={home}>К моим пакам</Button>
        </div>
      </div>
    </Page>
  );
}

function LinkSkeleton() {
  return (
    <div className={styles.skeleton} aria-hidden>
      <span className={layout.skeleton} style={{ width: 72, height: 72, borderRadius: 999 }} />
      <span className={layout.skeleton} style={{ width: "70%", height: 22 }} />
      <span className={layout.skeleton} style={{ width: "100%", height: 112, borderRadius: 20 }} />
    </div>
  );
}
