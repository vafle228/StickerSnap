import { useEffect, useState } from "react";
import { SwatchPicker, type AccentName } from "../ds";
import styles from "./Showcase.module.css";
import { Specimens } from "./Specimens";

const THEMES = [
  { value: "light", label: "Светлая" },
  { value: "dark", label: "Тёмная" },
] as const;

/** Dev-only page (/?showcase): every component in both themes, one accent at a time.
 *  Checked against the design system artifact instead of Storybook. */
export function Showcase() {
  const [accent, setAccent] = useState<AccentName>("indigo");

  // Windows open in a portal on <body>: they take the theme and accent of <html>.
  useEffect(() => {
    const root = document.documentElement;
    root.dataset.theme = "light";
    root.className = `ss-accent-${accent}`;
  }, [accent]);

  return (
    <div className={`ss-root ${styles.page}`}>
      <header className={styles.header}>
        <div>
          <h1 className={styles.title}>Витрина дизайн-системы</h1>
          <p className={styles.note}>
            32 компонента «Мягкий и плотный». Акцент меняет обе колонки сразу.
          </p>
        </div>
        <SwatchPicker value={accent} onChange={setAccent} />
      </header>
      <div className={styles.columns}>
        {THEMES.map((theme) => (
          <div
            key={theme.value}
            className={`ss-root ss-accent-${accent} ${styles.column}`}
            data-theme={theme.value}
          >
            <h2 className={styles.theme}>{theme.label}</h2>
            <Specimens id={theme.value} />
          </div>
        ))}
      </div>
    </div>
  );
}
