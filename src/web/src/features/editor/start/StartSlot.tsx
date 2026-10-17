import { DropZone } from "@stickersnap/ds";
import { useApp } from "../../../app/store";
import { mediaSrc } from "../../../shared/api/client";
import { useProjects } from "../../../shared/api/queries";
import { t } from "../../../shared/strings/ru";
import { useProjectActions } from "../../projects/actions";
import { useFileDrag } from "../../projects/drop";
import { when } from "../../projects/format";
import { ReadingFile } from "../../projects/ReadingFile";
import { useImport } from "../../projects/reading";
import { RecentCover } from "./RecentCover";
import styles from "./StartSlot.module.css";

const RECENT = 3;

/** The canvas's place while no project is open (artboard 6, «Стартовый экран»): a video dropped
 *  or picked here becomes a new project, and the three latest projects are one click away.
 *  Seen on the first launch and after a project is closed; the last one never opens by itself. */
export function StartSlot({ className }: { className?: string }) {
  const openProject = useApp((s) => s.openProject);
  const setSection = useApp((s) => s.setSection);
  const recent = useProjects().data?.slice(0, RECENT) ?? [];
  const reading = useImport((s) => s.reading);
  const over = useFileDrag((s) => s.over);
  const { create, openDropped } = useProjectActions();

  return (
    <div className={`${styles.start} ${className ?? ""}`}>
      {reading?.slow ? (
        <ReadingFile className={styles.zone} />
      ) : (
        <DropZone
          className={styles.zone}
          title={t("start.title")}
          hint={t("start.hint")}
          dropTitle={t("start.drop")}
          action={t("start.pick")}
          note={t("start.webm")}
          busy={reading !== undefined}
          dragging={over}
          onClick={() => void create()}
          onFiles={(files) => files[0] && openDropped(files[0])}
        />
      )}

      {recent.length > 0 && (
        <section className={styles.recent} aria-labelledby="start-recent">
          <div className={styles.recentHead}>
            <h2 id="start-recent" className={styles.recentTitle}>
              {t("start.recent")}
            </h2>
            <button type="button" className={styles.all} onClick={() => setSection("projects")}>
              {t("start.all")}
            </button>
          </div>
          <ul className={styles.list}>
            {recent.map((p) => (
              <li key={p.id}>
                <button type="button" className={styles.item} onClick={() => openProject(p.id)}>
                  <RecentCover src={mediaSrc(p.coverUrl)} />
                  <span className={styles.text}>
                    <span className={styles.name}>{p.name}</span>
                    <span className={styles.when}>{when(p.updatedAt)}</span>
                  </span>
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}
