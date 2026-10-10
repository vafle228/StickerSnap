import { Button, Card } from "../../../ds";
import { useApp } from "../../../app/store";
import { useProjects } from "../../../shared/api/queries";
import { t } from "../../../shared/strings/ru";
import styles from "./StartSlot.module.css";

const RECENT = 3;

/** The canvas's place while no project is open. The start screen of artboard 6 (drop zone and
 *  three recent projects) is its own task and replaces this; until then it opens recent ones. */
export function StartSlot({ className }: { className?: string }) {
  const openProject = useApp((s) => s.openProject);
  const setSection = useApp((s) => s.setSection);
  const projects = useProjects();
  const recent = projects.data?.slice(0, RECENT) ?? [];

  return (
    <Card variant="sunken" className={`${styles.start} ${className ?? ""}`}>
      <h2 className={styles.title}>{t("editor.start.title")}</h2>
      <p className={styles.text}>{t("editor.start.text")}</p>
      <div className={styles.actions}>
        {recent.map((p) => (
          <Button key={p.id} icon="film" onClick={() => openProject(p.id)}>
            {p.name}
          </Button>
        ))}
        <Button variant="ghost" icon="folder" onClick={() => setSection("projects")}>
          {t("editor.start.projects")}
        </Button>
      </div>
    </Card>
  );
}
