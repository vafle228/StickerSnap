import { Button, IconButton, SaveStatus } from "../../ds";
import { t } from "../../shared/strings/ru";
import styles from "./EditorHeader.module.css";
import { useEditor } from "./model/store";

/** Project name, save state, history and the two ways out: a new video in, a sticker to Telegram.
 *  Undo/redo, autosave, import and publishing are their own tasks; their controls wait here. */
export function EditorHeader({ ready }: { ready: boolean }) {
  const name = useEditor((s) => s.name);
  return (
    <header className={styles.header}>
      <h1 className={styles.name}>{ready ? name : t("nav.editor")}</h1>
      {ready && <SaveStatus state="saved" />}
      <span className={styles.spacer} />
      {ready && (
        <div className={styles.history}>
          <IconButton icon="undo" label={t("editor.undo")} flat disabled />
          <IconButton icon="redo" label={t("editor.redo")} flat disabled />
        </div>
      )}
      <Button icon="upload" disabled>
        {t("editor.import")}
      </Button>
      {ready && (
        <Button variant="prism" icon="send" disabled>
          {t("editor.publish")}
        </Button>
      )}
    </header>
  );
}
