import { Button, IconButton, SaveStatus } from "../../ds";
import { useApp } from "../../app/store";
import { t } from "../../shared/strings/ru";
import { useProjectActions } from "../projects/actions";
import { useImport } from "../projects/reading";
import styles from "./EditorHeader.module.css";
import { useSave } from "./model/autosave";
import { useEditor } from "./model/store";

/** «Назад», project name, save state, history and the two ways out: a new video in, a sticker to
 *  Telegram. «Назад» closes the project and returns to the section it was opened from; edits not
 *  yet saved still go to the server. «Импорт видео» starts a new project from a video.
 *  Publishing is its own task; its button waits. */
export function EditorHeader({ ready }: { ready: boolean }) {
  const name = useEditor((s) => s.name);
  const canUndo = useEditor((s) => s.past.length > 0);
  const canRedo = useEditor((s) => s.future.length > 0);
  const save = useSave();
  const reading = useImport((s) => s.reading !== undefined);
  const { create } = useProjectActions();
  const returnTo = useApp((s) => s.returnTo);
  const goBack = useApp((s) => s.goBack);
  const place = t(`editor.back.${returnTo}`);
  return (
    <header className={styles.header}>
      {/* The name says «Назад» too: «Проекты» alone would be the rail's button of the same name */}
      <Button
        variant="ghost"
        icon="chevron-left"
        aria-label={returnTo === "editor" ? place : t("editor.back.to", { place })}
        title={t("editor.back.hint")}
        onClick={goBack}
      >
        {place}
      </Button>
      <h1 className={styles.name}>{ready ? name : t("nav.editor")}</h1>
      {ready && <SaveStatus state={save.state} detail={save.detail} />}
      <span className={styles.spacer} />
      {ready && (
        <div className={styles.history}>
          <IconButton
            icon="undo"
            label={t("editor.undo")}
            flat
            disabled={!canUndo}
            onClick={() => useEditor.getState().undo()}
          />
          <IconButton
            icon="redo"
            label={t("editor.redo")}
            flat
            disabled={!canRedo}
            onClick={() => useEditor.getState().redo()}
          />
        </div>
      )}
      <Button icon="upload" disabled={reading} onClick={() => void create()}>
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
