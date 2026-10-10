import { Badge, Icon } from "../../ds";
import { useAccount, useAppInfo } from "../../shared/api/queries";
import type { AccountLink } from "../../shared/api/types";
import { t } from "../../shared/strings/ru";
import { SettingsSection } from "./Section";
import styles from "./sections.module.css";

type TelegramUser = Extract<AccountLink, { state: "linked" }>["user"];

interface AccountCard {
  /** The account itself, with a badge of its state */
  user?: TelegramUser;
  badge?: "linked" | "error";
  problem?: string;
  /** No account to show: what is going on instead */
  title?: string;
  text?: string;
}

/** The account card for each state of the link. */
function cardOf(link: AccountLink | undefined, failed: boolean): AccountCard {
  if (!link) return { title: t(failed ? "settings.bot.unlinked" : "settings.bot.loading") };
  switch (link.state) {
    case "linked":
      return { user: link.user, badge: "linked" };
    case "error":
      return link.user
        ? { user: link.user, badge: "error", problem: t(`settings.bot.${link.code}`) }
        : { title: t(`settings.bot.${link.code}`) };
    case "pending":
      return { title: t("settings.bot.pending") };
    case "unlinked":
      return { title: t("settings.bot.unlinked"), text: t("settings.bot.unlinkedText") };
  }
}

/** «Публикация в Telegram», first in the scroll. Stage 1a shows the link as the server has it;
 *  linking itself (the bot link, «Проверить связь», «Отвязать») comes with publishing in 1c.
 *  There is no bot token here: one shared bot (@StickerSnapAppBot). */
export function TelegramSection() {
  const account = useAccount();
  const bot = useAppInfo().data?.botUsername ?? "StickerSnapAppBot";
  const card = cardOf(account.data, account.isError);

  return (
    <SettingsSection id="bot" title={t("settings.bot.title")} text={t("settings.bot.text")}>
      <div className={styles.account}>
        <span className={styles.accountIcon} aria-hidden>
          <Icon name="send" size={20} />
        </span>
        <div className={styles.accountBody} aria-live="polite">
          {card.user ? (
            <>
              <div className={styles.accountLine}>
                <span className={styles.accountName}>
                  {card.user.username ? `@${card.user.username}` : card.user.firstName}
                </span>
                {card.badge === "linked" ? (
                  <Badge tone="success" icon="check">
                    {t("settings.bot.linked")}
                  </Badge>
                ) : (
                  <Badge tone="danger">{card.problem}</Badge>
                )}
              </div>
              <span className={styles.muted}>
                {t("settings.bot.via", { bot })} <span className={styles.mono}>_by_{bot}</span>
              </span>
            </>
          ) : (
            <>
              <span className={styles.accountName}>{card.title}</span>
              {card.text && <span className={styles.muted}>{card.text}</span>}
            </>
          )}
        </div>
      </div>
      <p className={styles.note}>{t("settings.bot.after")}</p>
    </SettingsSection>
  );
}
