import { Button, NavRail, type NavRailItem } from "../ds";
import { useEffect } from "react";
import { EditorScreen } from "../features/editor/EditorScreen";
import { ProjectsScreen } from "../features/projects/ProjectsScreen";
import { SettingsScreen } from "../features/settings/SettingsScreen";
import { t, type StringKey } from "../shared/strings/ru";
import { useLiveUpdates } from "../shared/api/live";
import { NotifyHost } from "../shared/notify";
import { OverlayHost } from "../shared/overlays";
import {
  THEME_MODES,
  useApplyTheme,
  useSetTheme,
  useTheme,
  type ThemeMode,
} from "../shared/theme/theme";
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
const MODE_LABELS: Record<ThemeMode, StringKey> = {
  system: "theme.system",
  light: "theme.light",
  dark: "theme.dark",
};

export function App() {
  useApplyTheme();
  useLiveUpdates();
  const section = useApp((s) => s.section);
  const setSection = useApp((s) => s.setSection);
  const mode = useTheme((s) => s.mode);
  const setTheme = useSetTheme();
  useNoStrayDrops();

  return (
    <div className={`ss-root ${styles.shell}`}>
      <NavRail
        className={styles.rail}
        narrow
        label={t("nav.label")}
        items={MAIN}
        bottomItems={BOTTOM}
        value={section}
        onChange={setSection}
      />
      <main className={styles.main}>
        {/* The editor is never unmounted: switching sections keeps its scene and playhead. */}
        <EditorScreen hidden={section !== "editor"} />
        {section === "settings" && <SettingsScreen />}
        {section === "projects" && <ProjectsScreen />}
        {section === "packs" && (
          <>
            <header className={styles.header}>
              <h1 className={styles.title}>{t("nav.packs")}</h1>
              <div className={styles.themes} role="group" aria-label={t("theme.label")}>
                {THEME_MODES.map((m) => (
                  <Button
                    key={m}
                    size="sm"
                    variant={m === mode ? "primary" : "ghost"}
                    aria-pressed={m === mode}
                    onClick={() => setTheme({ theme: m })}
                  >
                    {t(MODE_LABELS[m])}
                  </Button>
                ))}
              </div>
            </header>
            <p className={styles.empty}>{t("shell.empty")}</p>
          </>
        )}
      </main>
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
