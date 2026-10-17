import { Button } from "@stickersnap/ds";
import { Notice, Page } from "../shared/Layout";
import { useTelegram } from "../telegram/context";

/** Telegram's signature did not pass or is too old (artboard 14, state 4): a fresh button is
 *  in the chat with the bot, so «Закрыть» folds the Mini App. */
export function SignedOut() {
  const telegram = useTelegram();
  return (
    <Page label="Вход">
      <Notice
        tone="warning"
        title="Не получилось войти"
        text="Откройте StickerSnap заново из чата с ботом."
        action={
          <Button size="sm" onClick={() => telegram.close()}>
            Закрыть
          </Button>
        }
      />
    </Page>
  );
}
