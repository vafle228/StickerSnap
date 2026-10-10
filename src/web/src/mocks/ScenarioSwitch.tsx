import { Select } from "../ds";
import styles from "./ScenarioSwitch.module.css";
import { SCENARIOS, type Scenario } from "./scenarios";

const ITEMS = Object.entries(SCENARIOS).map(([value, label]) => ({
  value: value as Scenario,
  label,
}));

/** The dev switch of demo scenarios: reloads the page with another ?mock=. */
export function ScenarioSwitch({ scenario }: { scenario: Scenario }) {
  return (
    <div className={`ss-root ${styles.switch}`}>
      <Select
        label="Демо-данные"
        items={ITEMS}
        value={scenario}
        onChange={(next) => {
          const url = new URL(location.href);
          url.searchParams.set("mock", next);
          location.assign(url);
        }}
      />
    </div>
  );
}
