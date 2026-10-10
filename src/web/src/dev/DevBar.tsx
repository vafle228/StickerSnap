import { useLayoutEffect } from "react";
import { Badge, Segmented } from "../ds";
import { SCENARIOS, type Scenario } from "../mocks/scenarios";
import styles from "./DevBar.module.css";

type Source = "server" | Scenario;

const HEIGHT = "48px";

const ITEMS = [
  { value: "server" as const, label: "Сервер" },
  ...Object.entries(SCENARIOS).map(([value, label]) => ({ value: value as Scenario, label })),
];

/** Dev builds only: a strip above the window that says where the data comes from and switches
 *  it — the real server, or the demo data (MSW + fake hub) in one of its scenarios. It takes its
 *  own height off the window, so it never covers the screen under test. */
export function DevBar({ scenario }: { scenario?: Scenario }) {
  useLayoutEffect(() => {
    // A style property, not a class: the theme owns the class list of <html>.
    const html = document.documentElement.style;
    html.setProperty("--dev-bar-height", HEIGHT);
    return () => html.removeProperty("--dev-bar-height");
  }, []);

  const go = (next: Source) => {
    const url = new URL(location.href);
    if (next === "server") url.searchParams.delete("mock");
    else url.searchParams.set("mock", next);
    location.assign(url);
  };

  return (
    <div className={`ss-root ${styles.bar}`} role="region" aria-label="Режим разработки">
      <Badge tone="highlight">dev</Badge>
      <span className={styles.label}>Данные</span>
      <Segmented<Source>
        label="Откуда данные"
        className={styles.switch}
        items={ITEMS}
        value={scenario ?? "server"}
        onChange={go}
      />
      <span className={styles.hint}>
        {scenario ? "MSW и демо-хаб по контракту, F5 сбрасывает правки" : "127.0.0.1:5180"}
      </span>
      <a className={styles.link} href="/?showcase">
        Витрина ДС
      </a>
    </div>
  );
}
