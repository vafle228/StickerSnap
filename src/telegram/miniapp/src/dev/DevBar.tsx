import { useSyncExternalStore } from "react";
import { Badge, Icon, Segmented } from "@stickersnap/ds";
import { SCENARIOS, type Scenario } from "../mocks/scenarios";
import type { FakeTelegram } from "../telegram/fakeTelegram";
import type { ColorScheme } from "../telegram/webapp";
import styles from "./DevBar.module.css";

const THEMES = [
  { value: "light" as const, label: "Светлая" },
  { value: "dark" as const, label: "Тёмная" },
];

/** Dev only (?mock, ?tma): a strip that switches the demo scenario and Telegram's theme, and under
 *  it a stand-in for Telegram's header with its «Закрыть» / «Назад» and «⋯ → Настройки», so the
 *  Mini App can be walked through in a plain browser. No scenario: the real bot service. */
export function DevBar({ telegram, scenario }: { telegram: FakeTelegram; scenario?: Scenario }) {
  const back = useSyncExternalStore(telegram.subscribe, () => telegram.calls.backButton);
  const settings = useSyncExternalStore(telegram.subscribe, () => telegram.calls.settingsShown);
  const scheme = useSyncExternalStore(telegram.onThemeChanged, () => telegram.colorScheme());

  const reload = (key: string, value: string) => {
    const url = new URL(location.href);
    url.searchParams.set(key, value);
    location.assign(url);
  };
  const setTheme = (next: ColorScheme) => {
    const url = new URL(location.href);
    url.searchParams.set("theme", next);
    history.replaceState(null, "", url);
    telegram.setColorScheme(next);
  };

  return (
    <div className={`ss-root ${styles.top}`}>
      <div className={styles.bar} role="region" aria-label="Режим разработки">
        <Badge tone="highlight">dev</Badge>
        {scenario ? (
          <select
            className={styles.select}
            aria-label="Демо-данные"
            value={scenario}
            onChange={(e) => reload("mock", e.target.value)}
          >
            {Object.entries(SCENARIOS).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        ) : (
          <span className={styles.live}>настоящий сервис</span>
        )}
        <Segmented<ColorScheme>
          label="Тема Telegram"
          items={THEMES}
          value={scheme}
          onChange={setTheme}
        />
      </div>
      <div className={styles.header} aria-label="Шапка Telegram (имитация)">
        <button
          type="button"
          className={styles.tgButton}
          onClick={() => (back ? telegram.pressBack() : telegram.close())}
        >
          {back && <Icon name="chevron-left" size={22} strokeWidth={2.2} />}
          {back ? "Назад" : "Закрыть"}
        </button>
        <div className={styles.title}>
          <b>StickerSnap</b>
          <span>мини-приложение</span>
        </div>
        <button
          type="button"
          className={styles.tgMenu}
          aria-label="Меню Telegram: Настройки"
          title="⋯ → Настройки"
          disabled={!settings}
          onClick={() => telegram.pressSettings()}
        >
          <Icon name="more" size={22} />
        </button>
      </div>
    </div>
  );
}
