import type { UseMutationResult } from "@tanstack/react-query";
import { useState, type ReactNode } from "react";
import { Badge, Banner, Button, Icon } from "../../ds";
import { useApp } from "../../app/store";
import { useAccount, useAccountActions, useAppInfo } from "../../shared/api/queries";
import type { AccountLink, Problem } from "../../shared/api/types";
import { notifyProblem, PROBLEMS, problemText, toProblem } from "../../shared/notify/problems";
import { platform } from "../../shared/platform";
import { t } from "../../shared/strings/ru";
import { SettingsSection } from "./Section";
import styles from "./sections.module.css";

type TelegramUser = Extract<AccountLink, { state: "linked" }>["user"];
type AccountAction = UseMutationResult<AccountLink, Error, void>;

/** A failed action shown in the section, with the action again. */
interface Failure {
  problem: Problem;
  retry: () => void;
}

/** «Публикация в Telegram», first in the scroll: the four states of the link (artboard 5) right
 *  in the section. «Открыть бота» gets a one-time code from the server and opens the bot; the
 *  server waits for Start and says so with accountChanged. There is no bot token here: one
 *  shared bot (@StickerSnapAppBot). */
export function TelegramSection() {
  const account = useAccount();
  const actions = useAccountActions();
  const setSection = useApp((s) => s.setSection);
  const bot = useAppInfo().data?.botUsername ?? "StickerSnapAppBot";
  const link = account.data;

  const [failure, setFailure] = useState<Failure>();
  // «Готово» after Start pressed while the section is open; the account card on any other visit
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

  const openBot = () =>
    run(actions.start, (next) => {
      if (next.state === "pending") platform.openExternal(next.deepLink);
    });
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
      </div>
    );
  } else if (link.state === "pending") {
    body = (
      <div className={styles.linkStep}>
        <h3 className={styles.stepTitle}>{t("settings.bot.wait.title")}</h3>
        <p className={styles.note}>{t("settings.bot.wait.text")}</p>
        <div className={styles.waiting} role="status">
          <Icon name="spinner" size={18} strokeWidth={2} />
          {t("settings.bot.pending")}
        </div>
        <div className={styles.stepActions}>
          <Button icon="external" onClick={() => platform.openExternal(link.deepLink)}>
            {t("settings.bot.openAgain")}
          </Button>
          <Button variant="ghost" disabled={busy} onClick={drop}>
            {t("common.cancel")}
          </Button>
        </div>
      </div>
    );
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

/** The sunken account card: who is linked and its badge, or what is going on instead. */
function AccountCard({
  user,
  problem,
  bot,
  title,
  actions,
}: {
  user?: TelegramUser;
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
              <span className={styles.accountName}>{nameOf(user)}</span>
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
