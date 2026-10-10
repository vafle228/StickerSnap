import { NavRail, type NavRailItem } from "../ds";
import { useEffect } from "react";
import { EditorScreen } from "../features/editor/EditorScreen";
import { PacksScreen } from "../features/packs/PacksScreen";
import { PublishWatch } from "../features/packs/PublishWatch";
import { ProjectsScreen } from "../features/projects/ProjectsScreen";
import { SettingsScreen } from "../features/settings/SettingsScreen";
import { t } from "../shared/strings/ru";
import { useLiveUpdates } from "../shared/api/live";
import { useJobs } from "../shared/api/queries";
import { NotifyHost } from "../shared/notify";
import { OverlayHost } from "../shared/overlays";
import { useApplyTheme } from "../shared/theme/theme";
import styles from "./App.module.css";
import { useApp, type Section } from "./store";

const MAIN: NavRailItem<Section>[] = [
  { value: "editor", label: t("nav.editor"), icon: "film" },
  { value: "projects", label: t("nav.projects"), icon: "folder" },
  { value: "packs", label: t("nav.packs"), icon: "sticker" },
];
const BOTTOM: NavRailItem<Section>[] = [
  { value: "settings", label: t("nav.settings"), icon: "settings" },
];

export function App() {
  useApplyTheme();
  useLiveUpdates();
  const section = useApp((s) => s.section);
  const setSection = useApp((s) => s.setSection);
  useNoStrayDrops();
  // A send to a pack runs: a dot on «Мои паки» until it ends (artboard 12)
  const sending = (useJobs().data ?? []).some(
    (j) => j.kind === "publish" && (j.state === "running" || j.state === "queued"),
  );

  return (
    <div className={`ss-root ${styles.shell}`}>
      <NavRail
        className={styles.rail}
        narrow
        label={t("nav.label")}
        items={MAIN.map((item) => (item.value === "packs" ? { ...item, dot: sending } : item))}
        bottomItems={BOTTOM}
        value={section}
        onChange={setSection}
      />
      <main className={styles.main}>
        {/* The editor is never unmounted: switching sections keeps its scene and playhead. */}
        <EditorScreen hidden={section !== "editor"} />
        {section === "settings" && <SettingsScreen />}
        {section === "projects" && <ProjectsScreen />}
        {section === "packs" && <PacksScreen />}
      </main>
      <PublishWatch />
      <OverlayHost />
      <NotifyHost />
    </div>
  );
}

/** A file dropped beside a drop zone must not make the browser leave the app to show it. */
function useNoStrayDrops() {
  useEffect(() => {
    const stop = (e: DragEvent) => e.preventDefault();
    window.addEventListener("dragover", stop);
    window.addEventListener("drop", stop);
    return () => {
      window.removeEventListener("dragover", stop);
      window.removeEventListener("drop", stop);
    };
  }, []);
}
