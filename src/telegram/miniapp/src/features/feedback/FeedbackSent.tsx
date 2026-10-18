import { Button, Icon } from "@stickersnap/ds";
import { useNav } from "../../app/nav";
import { Page } from "../../shared/Layout";
import layout from "../../shared/layout.module.css";
import styles from "./feedback.module.css";

/** «Спасибо, отзыв у нас» (18·7): the whole screen in the «готово» tone, alone on the stack, so
 *  Telegram's header says «Закрыть». */
export function FeedbackSent() {
  const home = useNav((s) => s.home);
  return (
    <Page label="Отзыв отправлен">
      <div className={layout.empty} role="status">
        <span className={styles.big}>
          <Icon name="check" size={34} strokeWidth={2} />
        </span>
        <h1 className={layout.emptyTitle}>Спасибо, отзыв у нас</h1>
        <p className={layout.emptyText}>
          Если разрешили писать, ответим в этом чате. Ещё что-то? Напишите снова.
        </p>
        <div className={layout.emptyAction}>
          <Button onClick={home}>К моим пакам</Button>
        </div>
      </div>
    </Page>
  );
}
