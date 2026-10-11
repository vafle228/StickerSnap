import { DropZone } from "../../../ds";
import { t } from "../../../shared/strings/ru";
import { useProjectActions } from "../../projects/actions";
import { useFileDrag } from "../../projects/drop";
import { ReadingFile } from "../../projects/ReadingFile";
import styles from "./VideoStage.module.css";

/** With a project open, the canvas is where a carried file lands (artboard 3c, 0) and where it is
 *  read (A). The project under it stays as it is: a video becomes a new project, a .webm opens
 *  its check, and a file that cannot be read leaves only a popup. */
export function StageDrop() {
  const over = useFileDrag((s) => s.over);
  const { openDropped } = useProjectActions();
  return (
    <>
      {over && (
        <div className={styles.over}>
          <DropZone
            className={styles.overZone}
            dragging
            dropTitle={t("start.drop")}
            hint={t("editor.dropHint")}
            onFiles={(files) => files[0] && openDropped(files[0])}
          />
        </div>
      )}
      <ReadingFile className={styles.over} />
    </>
  );
}
