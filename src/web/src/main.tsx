import { StrictMode, type ReactNode } from "react";
import { createRoot } from "react-dom/client";
import "./ds/index.css";
import "./app/global.css";
import { App } from "./app/App";
import { Providers } from "./app/Providers";

const root = document.getElementById("root");
if (!root) throw new Error("#root is missing in index.html");

const render = (node: ReactNode) => createRoot(root).render(<StrictMode>{node}</StrictMode>);
const params = new URLSearchParams(location.search);

// Dev only, both branches are dropped from production builds:
// /?showcase opens the design system showcase, /?mock runs the SPA on demo data
// (/?mock=offline and the other scenarios of mocks/scenarios.ts).
if (import.meta.env.DEV && params.has("showcase")) {
  void import("./showcase/Showcase").then(({ Showcase }) => render(<Showcase />));
} else if (import.meta.env.DEV && params.has("mock")) {
  void Promise.all([
    import("./mocks/start"),
    import("./mocks/ScenarioSwitch"),
    import("./mocks/scenarios"),
  ]).then(async ([{ startMocks }, { ScenarioSwitch }, { isScenario }]) => {
    const asked = params.get("mock");
    const scenario = isScenario(asked) ? asked : "ok";
    await startMocks(scenario);
    render(
      <Providers>
        <App />
        <ScenarioSwitch scenario={scenario} />
      </Providers>,
    );
  });
} else {
  render(
    <Providers>
      <App />
    </Providers>,
  );
}
