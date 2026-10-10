import { useSyncExternalStore } from "react";
import { ACCENTS, Segmented, SwatchPicker } from "../../ds";
import { t, type StringKey } from "../../shared/strings/ru";
import {
  THEME_MODES,
  useSetTheme,
  useTheme,
  type AccentName,
  type ThemeMode,
} from "../../shared/theme/theme";
import { SettingsSection } from "./Section";
import styles from "./sections.module.css";

const MODE_LABELS: Record<ThemeMode, StringKey> = {
  light: "theme.light",
  dark: "theme.dark",
  system: "theme.system",
};
// Светлая, Тёмная, Как в системе: the order of artboard 5
const MODES = [...THEME_MODES.filter((m) => m !== "system"), "system" as const].map((m) => ({
  value: m,
  label: t(MODE_LABELS[m]),
}));

const DEFAULT_ACCENT: AccentName = "indigo";

const darkQuery = () => window.matchMedia("(prefers-color-scheme: dark)");

function subscribe(onChange: () => void) {
  const query = darkQuery();
  query.addEventListener("change", onChange);
  return () => query.removeEventListener("change", onChange);
}

/** The OS theme right now, for the line under «Как в системе». */
function useSystemDark() {
  return useSyncExternalStore(subscribe, () => darkQuery().matches);
}

/** «Оформление» (R20): the theme and the accent change the window at once. */
export function LookSection() {
  const mode = useTheme((s) => s.mode);
  const accent = useTheme((s) => s.accent);
  const setTheme = useSetTheme();
  const systemDark = useSystemDark();
  const accentName = ACCENTS.find((a) => a.value === accent)?.label ?? accent;

  return (
    <SettingsSection id="look" title={t("settings.look")}>
      <div className={styles.columns}>
        <div className={styles.group}>
          <span className={styles.label}>{t("settings.theme")}</span>
          <Segmented<ThemeMode>
            label={t("settings.theme")}
            items={MODES}
            value={mode}
            onChange={(theme) => setTheme({ theme })}
          />
          {mode === "system" && (
            <span className={styles.muted}>
              {t(systemDark ? "settings.theme.nowDark" : "settings.theme.nowLight")}
            </span>
          )}
        </div>
        <div className={styles.group}>
          <span className={styles.label}>{t("settings.accent")}</span>
          <SwatchPicker<AccentName>
            label={t("settings.accent")}
            showLabel={false}
            value={accent}
            onChange={(next) => setTheme({ accent: next })}
          />
          <span className={styles.muted}>
            {accent === DEFAULT_ACCENT
              ? t("settings.accent.default", { name: accentName })
              : accentName}
          </span>
        </div>
      </div>
    </SettingsSection>
  );
}
