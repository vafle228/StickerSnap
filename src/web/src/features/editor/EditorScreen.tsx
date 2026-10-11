import { useEffect } from "react";
import { useApp } from "../../app/store";
import { useProject } from "../../shared/api/queries";
import { useHotkey } from "../../shared/hotkeys";
import { notifyProblem } from "../../shared/notify";
import { t } from "../../shared/strings/ru";
import { EditorHeader } from "./EditorHeader";
import styles from "./EditorScreen.module.css";
import { Inspector } from "./inspector/Inspector";
import { useEditor } from "./model/store";
import { useRenderJobs } from "./render/useRenderJobs";
import { MissingSource } from "./stage/MissingSource";
import { VideoStage } from "./stage/VideoStage";
import { StartSlot } from "./start/StartSlot";
import { TimelineSlot } from "./timeline/TimelineSlot";

/** Artboard 3a. Stays mounted while another section is open (no router), so the playhead,
 *  the zoom and the scene being edited are where the user left them. */
export function EditorScreen({ hidden }: { hidden: boolean }) {
  const projectId = useApp((s) => s.projectId);
  const closeProject = useApp((s) => s.closeProject);
  const project = useProject(projectId);
  const loadedId = useEditor((s) => s.projectId);
  const load = useEditor((s) => s.load);
  const unload = useEditor((s) => s.unload);
  const setPlaying = useEditor((s) => s.setPlaying);
  useRenderJobs(project.data);

  useEffect(() => {
    if (project.data && project.data.id !== loadedId) load(project.data);
  }, [project.data, loadedId, load]);

  useEffect(() => {
    if (!projectId) unload();
  }, [projectId, unload]);

  // A project that cannot be opened: one popup, and the start screen takes the canvas back.
  useEffect(() => {
    if (!project.error) return;
    notifyProblem(project.error, { key: "project-open" });
    closeProject();
  }, [project.error, closeProject]);

  useEffect(() => {
    if (hidden) setPlaying(false);
  }, [hidden, setPlaying]);

  const ready = projectId !== undefined && loadedId === projectId;
  useHotkey("playPause", () => setPlaying(!useEditor.getState().playing), ready && !hidden);

  return (
    <div className={styles.editor} hidden={hidden}>
      {/* The start screen has the window to itself (artboard 6): no name, no import, no publish */}
      {projectId && <EditorHeader ready={ready} />}
      <div className={styles.body}>
        <div className={styles.work}>
          {ready ? (
            <>
              <MissingSource />
              <VideoStage className={styles.stage} />
              <TimelineSlot active={!hidden} />
            </>
          ) : projectId ? (
            <p className={styles.loading} role="status">
              {t("editor.loading")}
            </p>
          ) : (
            <StartSlot className={styles.stage} />
          )}
        </div>
        {ready && (
          <aside className={styles.aside} aria-label={t("editor.inspector")}>
            <Inspector />
          </aside>
        )}
      </div>
    </div>
  );
}
