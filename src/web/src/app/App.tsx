import { Button } from "../ds/components/Button";
import { NavRail, type NavRailItem } from "../ds/components/NavRail";
import { t, type StringKey } from "../shared/strings/ru";
import { THEME_MODES, useApplyTheme, useTheme, type ThemeMode } from "../shared/theme/theme";
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
const TITLES: Record<Section, StringKey> = {
  editor: "nav.editor",
  projects: "nav.projects",
  packs: "nav.packs",
  settings: "nav.settings",
};
const MODE_LABELS: Record<ThemeMode, StringKey> = {
  system: "theme.system",
  light: "theme.light",
  dark: "theme.dark",
};

export function App() {
  useApplyTheme();
  const section = useApp((s) => s.section);
  const setSection = useApp((s) => s.setSection);
  const mode = useTheme((s) => s.mode);
  const setMode = useTheme((s) => s.setMode);

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
        <header className={styles.header}>
          <h1 className={styles.title}>{t(TITLES[section])}</h1>
          <div className={styles.themes} role="group" aria-label={t("theme.label")}>
            {THEME_MODES.map((m) => (
              <Button
                key={m}
                size="sm"
                variant={m === mode ? "primary" : "ghost"}
                aria-pressed={m === mode}
                onClick={() => setMode(m)}
              >
                {t(MODE_LABELS[m])}
              </Button>
            ))}
          </div>
        </header>
        <p className={styles.empty}>{t("shell.empty")}</p>
      </main>
    </div>
  );
}
