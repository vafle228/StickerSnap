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

// Dev tools, dropped from the production build (vite.config.ts): /?showcase opens the design system
// showcase, /?mock runs the SPA on demo data (/?mock=offline and the other scenarios of
// mocks/scenarios.ts), and the dev bar on top switches between the server and the scenarios.
async function startDev() {
  if (params.has("showcase")) {
    const { Showcase } = await import("./showcase/Showcase");
    render(<Showcase />);
    return;
  }
  const [{ DevBar }, { isScenario }] = await Promise.all([
    import("./dev/DevBar"),
    import("./mocks/scenarios"),
  ]);
  let scenario;
  if (params.has("mock")) {
    const asked = params.get("mock");
    scenario = isScenario(asked) ? asked : "ok";
    const { startMocks } = await import("./mocks/start");
    await startMocks(scenario);
  }
  render(
    <Providers>
      <DevBar scenario={scenario} />
      <App />
    </Providers>,
  );
}

if (import.meta.env.DEV_TOOLS) {
  void startDev();
} else {
  render(
    <Providers>
      <App />
    </Providers>,
  );
}
