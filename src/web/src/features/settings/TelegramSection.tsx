import type { UseMutationResult } from "@tanstack/react-query";
import { useEffect, useState, type ReactNode } from "react";
import { Badge, Banner, Button, Icon } from "@stickersnap/ds";
import { useApp } from "../../app/store";
import { useAccount, useAccountActions, useAppInfo } from "../../shared/api/queries";
import type { AccountLink, Problem } from "../../shared/api/types";
import { notifyProblem, PROBLEMS, problemText, toProblem } from "../../shared/notify/problems";
import { platform } from "../../shared/platform";
import { t } from "../../shared/strings/ru";
import { countdown } from "../packs/format";
import { LinkQr } from "./LinkQr";
import { SettingsSection } from "./Section";
import styles from "./sections.module.css";

type TelegramUser = Extract<AccountLink, { state: "linked" }>["user"];
type AccountAction = UseMutationResult<AccountLink, Error, void>;

/** A failed action shown in the section, with the action again. */
interface Failure {
  problem: Problem;
  retry: () => void;
}

/** «Публикация в Telegram», first in the scroll: the four states of the link (artboards 5 and 18)
 *  right in the section. «Привязать» gets a code from the server and shows its link to the Mini
 *  App as a button, a copy and a QR (artboard 18, section 4); the server waits for the user's
 *  answer in Telegram and says so with accountChanged. There is no bot token here: one shared
 *  bot (@StickerSnapAppBot). */
export function TelegramSection() {
  const account = useAccount();
  const actions = useAccountActions();
  const setSection = useApp((s) => s.setSection);
  const bot = useAppInfo().data?.botUsername ?? "StickerSnapAppBot";
  const link = account.data;

  const [failure, setFailure] = useState<Failure>();
  // «Готово» after the user confirmed while the section is open; the account card on any other visit
  const [seen, setSeen] = useState(link?.state);
  const [justLinked, setJustLinked] = useState(false);
  if (link?.state !== seen) {
    setSeen(link?.state);
    setJustLinked(seen === "pending" && link?.state === "linked");
  }

  function run(action: AccountAction, then?: (next: AccountLink) => void) {
    action.mutate(undefined, {
      onSuccess: (next) => {
        setFailure(undefined);
        then?.(next);
      },
      onError: (error) => {
        const problem = toProblem(error);
        // No bot token, no connection: said in the section; anything else is a popup
        if (PROBLEMS[problem.code].form === "banner") {
          setFailure({ problem, retry: () => run(action, then) });
        } else {
          notifyProblem(error, { key: "account" });
        }
      },
    });
  }

  // The link waits on screen with its QR: Telegram may be only on the phone
  const openBot = () => run(actions.start);
  const drop = () => run(actions.drop);
  const check = () => run(actions.check);
  const busy = actions.start.isPending || actions.drop.isPending || actions.check.isPending;

  let body: ReactNode;
  if (!link) {
    body = (
      <AccountCard title={t(account.isError ? "settings.bot.unlinked" : "settings.bot.loading")} />
    );
  } else if (link.state === "unlinked") {
    body = (
      <div className={styles.linkStep}>
        <h3 className={styles.stepTitle}>{t("settings.bot.link.title")}</h3>
        <p className={styles.note}>{t("settings.bot.link.text")}</p>
        <ol className={styles.steps}>
          <li>{t("settings.bot.link.step1")}</li>
          <li>{t("settings.bot.link.step2")}</li>
          <li>{t("settings.bot.link.step3")}</li>
        </ol>
        <div className={styles.stepActions}>
          <Button variant="primary" icon="send" disabled={busy} onClick={openBot}>
            {t("settings.bot.open")}
          </Button>
        </div>
        {link.reason && (
          <p className={styles.hint} role="status">
            {t(`settings.bot.${link.reason}`)}
          </p>
        )}
      </div>
    );
  } else if (link.state === "pending") {
    body = <PendingLink link={link} busy={busy} onCancel={drop} />;
  } else {
    body = (
      <>
        {justLinked && link.state === "linked" && (
          <Banner
            tone="success"
            title={t("settings.bot.done", { name: nameOf(link.user) })}
            action={
              <Button size="sm" onClick={() => setSection("editor")}>
                {t("settings.bot.toEditor")}
              </Button>
            }
          />
        )}
        <AccountCard
          user={link.user}
          device={link.device}
          problem={link.state === "error" ? link.code : undefined}
          bot={bot}
          actions={
            <>
              <Button disabled={busy} onClick={check}>
                {t("settings.bot.check")}
              </Button>
              <Button variant="ghost" className={styles.unlink} disabled={busy} onClick={drop}>
                {t("settings.bot.unlink")}
              </Button>
            </>
          }
        />
        {link.state === "error" && (
          <Banner
            tone={link.code === "bot_blocked" ? "danger" : "warning"}
            title={t(`settings.bot.${link.code}`)}
            description={t(`settings.bot.${link.code}.text`)}
            action={
              <Button
                size="sm"
                disabled={busy}
                onClick={link.code === "bot_blocked" ? openBot : check}
              >
                {t("common.retry")}
              </Button>
            }
          />
        )}
      </>
    );
  }

  return (
    <SettingsSection id="bot" title={t("settings.bot.title")} text={t("settings.bot.text")}>
      <div aria-live="polite" className={styles.group}>
        {body}
      </div>
      {failure && (
        <Banner
          tone="danger"
          title={problemText(failure.problem).title}
          description={problemText(failure.problem).text}
          action={
            <Button size="sm" disabled={busy} onClick={failure.retry}>
              {t("common.retry")}
            </Button>
          }
        />
      )}
      {(link?.state === "linked" || link?.state === "error") && (
        <p className={styles.note}>{t("settings.bot.after")}</p>
      )}
    </SettingsSection>
  );
}

function nameOf(user: TelegramUser) {
  return user.username ? `@${user.username}` : user.firstName;
}

/** Waiting for the answer in Telegram (artboard 18, section 4): the QR, the link as a button and
 *  a copy, «Отмена» and how long the link lives. Its end comes from the server. */
function PendingLink({
  link,
  busy,
  onCancel,
}: {
  link: Extract<AccountLink, { state: "pending" }>;
  busy: boolean;
  onCancel: () => void;
}) {
  const [now, setNow] = useState(() => Date.now());
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, []);

  useEffect(() => {
    if (!copied) return undefined;
    const timer = setTimeout(() => setCopied(false), 2000);
    return () => clearTimeout(timer);
  }, [copied]);

  const copy = () => {
    void navigator.clipboard?.writeText(link.deepLink).then(
      () => setCopied(true),
      () => {},
    );
  };

  return (
    <div className={styles.pending}>
      <LinkQr link={link.deepLink} label={t("settings.bot.qr")} />
      <div className={styles.pendingBody}>
        <Badge tone="info" icon="info">
          {t("settings.bot.pending")}
        </Badge>
        <p className={styles.note}>{t("settings.bot.wait.text")}</p>
        <div className={styles.stepActions}>
          <Button
            variant="primary"
            icon="external"
            onClick={() => platform.openExternal(link.deepLink)}
          >
            {t("settings.bot.openTelegram")}
          </Button>
          <Button icon={copied ? "check" : undefined} onClick={copy}>
            {t(copied ? "common.copied" : "settings.bot.copyLink")}
          </Button>
          <Button variant="ghost" disabled={busy} onClick={onCancel}>
            {t("common.cancel")}
          </Button>
        </div>
        <span className={styles.hint} role="timer">
          {t("settings.bot.expiresIn", { time: countdown(Date.parse(link.expiresAt) - now) })}
        </span>
      </div>
    </div>
  );
}

/** The sunken account card: who is linked and its badge, or what is going on instead. */
function AccountCard({
  user,
  device,
  problem,
  bot,
  title,
  actions,
}: {
  user?: TelegramUser;
  device?: string;
  problem?: "bot_blocked" | "network_unavailable";
  bot?: string;
  title?: string;
  actions?: ReactNode;
}) {
  return (
    <div className={styles.account}>
      <span className={styles.accountIcon} aria-hidden>
        <Icon name="send" size={20} />
      </span>
      <div className={styles.accountBody}>
        {user ? (
          <>
            <div className={styles.accountLine}>
              <span className={styles.accountName}>
                {device ? `${nameOf(user)}, ${device}` : nameOf(user)}
              </span>
              {problem ? (
                <Badge tone="danger">{t(`settings.bot.${problem}`)}</Badge>
              ) : (
                <Badge tone="success" icon="check">
                  {t("settings.bot.linked")}
                </Badge>
              )}
            </div>
            <span className={styles.muted}>
              {t("settings.bot.via", { bot: bot ?? "" })}{" "}
              <span className={styles.mono}>_by_{bot}</span>
            </span>
          </>
        ) : (
          <span className={styles.accountName}>{title}</span>
        )}
      </div>
      {actions && <div className={styles.accountActions}>{actions}</div>}
    </div>
  );
}
