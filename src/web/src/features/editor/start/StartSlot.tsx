import { DropZone } from "../../../ds";
import { useApp } from "../../../app/store";
import { mediaSrc } from "../../../shared/api/client";
import { useProjects } from "../../../shared/api/queries";
import { t } from "../../../shared/strings/ru";
import { useImport, useProjectActions } from "../../projects/actions";
import { when } from "../../projects/format";
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
  const { create } = useProjectActions();

  return (
    <div className={`${styles.start} ${className ?? ""}`}>
      <DropZone
        className={styles.zone}
        title={reading ? t("start.reading") : t("start.title")}
        hint={reading ? null : t("start.hint")}
        dropTitle={t("start.drop")}
        action={reading ? undefined : t("start.pick")}
        busy={reading}
        onClick={() => void create()}
        onFiles={(files) => files[0] && void create(files[0])}
      />

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
