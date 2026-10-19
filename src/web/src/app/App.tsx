import { useEffect } from "react";
import { NavRail, type NavRailItem } from "@stickersnap/ds";
import { EditorScreen } from "../features/editor/EditorScreen";
import { openBugReport, openFeedback } from "../features/feedback/openFeedback";
import { PacksScreen } from "../features/packs/PacksScreen";
import { PublishWatch } from "../features/packs/PublishWatch";
import { useProjectActions } from "../features/projects/actions";
import { useWindowDrop } from "../features/projects/drop";
import { ProjectsScreen } from "../features/projects/ProjectsScreen";
import { SettingsScreen } from "../features/settings/SettingsScreen";
import { t } from "../shared/strings/ru";
import { useLiveUpdates } from "../shared/api/live";
import { useJobs } from "../shared/api/queries";
import { NotifyHost } from "../shared/notify";
import { OverlayHost } from "../shared/overlays";
import { platform } from "../shared/platform";
import {
  notifyCrash,
  ScreenBoundary,
  setReportWindow,
  startCapture,
  trail,
} from "../shared/report";
import { CrashProbe } from "../shared/report/probe";
import { useApplyTheme } from "../shared/theme/theme";
import styles from "./App.module.css";
import { useApp, type Section } from "./store";

// «Отзыв» opens a window over any section instead of being one (artboard 19)
type RailItem = Section | "feedback";

const MAIN: NavRailItem<RailItem>[] = [
  { value: "editor", label: t("nav.editor"), icon: "film" },
  { value: "projects", label: t("nav.projects"), icon: "folder" },
  { value: "packs", label: t("nav.packs"), icon: "sticker" },
];
const BOTTOM: NavRailItem<RailItem>[] = [
  { value: "feedback", label: t("nav.feedback"), icon: "feedback" },
  { value: "settings", label: t("nav.settings"), icon: "settings" },
];

// «Сообщить об ошибке» anywhere opens the feedback window with the error (artboard 19)
setReportWindow(openBugReport);
let crashAsked = false;
let lastSection: Section | undefined;

/** What a bug's report needs before anyone asks (artboard 19, section 4): the clicks and the
 *  sections on the trail, the errors nothing caught, the crash of the last launch. */
function useErrorCapture(section: Section) {
  useEffect(() => startCapture(), []);
  useEffect(() => {
    // Once per visit, not once per effect run
    if (section === lastSection) return;
    lastSection = section;
    trail.record(`Раздел: ${t(`nav.${section}`)}`);
  }, [section]);
  useEffect(() => {
    // Asked once per page: the shell hands the crash over only once
    if (crashAsked) return;
    crashAsked = true;
    void platform.lastCrash().then((crash) => {
      if (crash) notifyCrash(crash);
    });
  }, []);
}

export function App() {
  useApplyTheme();
  useLiveUpdates();
  const section = useApp((s) => s.section);
  useErrorCapture(section);
  const setSection = useApp((s) => s.setSection);
  // A video dropped anywhere becomes a project, a .webm opens its check (R1, R8).
  useWindowDrop(useProjectActions().openDropped);
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
        onChange={(item) => (item === "feedback" ? openFeedback() : setSection(item))}
      />
      <main className={styles.main}>
        {/* The editor is never unmounted: switching sections keeps its scene and playhead.
            A section that throws gives way to «Что-то сломалось» (19·3), the menu stays. */}
        <ScreenBoundary resetKey="editor" hidden={section !== "editor"}>
          {section === "editor" && <CrashProbe />}
          <EditorScreen hidden={section !== "editor"} />
        </ScreenBoundary>
        {section !== "editor" && (
          <ScreenBoundary resetKey={section}>
            <CrashProbe />
            {section === "settings" && <SettingsScreen />}
            {section === "projects" && <ProjectsScreen />}
            {section === "packs" && <PacksScreen />}
          </ScreenBoundary>
        )}
      </main>
      <PublishWatch />
      <OverlayHost />
      <NotifyHost />
    </div>
  );
}
