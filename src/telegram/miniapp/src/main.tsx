import { StrictMode, type ReactNode } from "react";
import { createRoot } from "react-dom/client";
import "@stickersnap/ds/index.css";
import "./app/global.css";
import { App } from "./app/App";
import { boot } from "./app/boot";
import { Providers } from "./app/Providers";
import { createTelegram, type Telegram } from "./telegram/bridge";
import { createFakeTelegram } from "./telegram/fakeTelegram";

const root = document.getElementById("root");
if (!root) throw new Error("#root is missing in index.html");

const render = (telegram: Telegram, node: ReactNode) => {
  boot(telegram);
  createRoot(root).render(
    <StrictMode>
      <Providers telegram={telegram}>{node}</Providers>
    </StrictMode>,
  );
};

/** Inside Telegram the real bridge; anywhere else one with no signature, which shows
 *  «Не получилось войти». */
function realTelegram(): Telegram {
  const webApp = window.Telegram?.WebApp;
  return webApp ? createTelegram(webApp) : createFakeTelegram();
}

// Dev tools, dropped from the production build (vite.config.ts): /?mock runs the Mini App in a
// plain browser on demo data (/?mock=offline and the other scenarios of mocks/scenarios.ts),
// with a dev bar that plays Telegram's header and switches the theme and the scenario.
async function startDev() {
  const params = new URLSearchParams(location.search);
  if (!params.has("mock")) {
    render(realTelegram(), <App />);
    return;
  }
  const [{ isScenario }, { startMocks }, { DevBar }] = await Promise.all([
    import("./mocks/scenarios"),
    import("./mocks/start"),
    import("./dev/DevBar"),
  ]);
  const asked = params.get("mock");
  const scenario = isScenario(asked) ? asked : "ok";
  await startMocks(scenario);
  const telegram = createFakeTelegram({
    initData: "query_id=mock&user=%7B%22id%22%3A1%7D&hash=mock",
    startParam: params.get("startapp") ?? undefined,
    colorScheme: params.get("theme") === "dark" ? "dark" : "light",
  });
  render(
    telegram,
    <>
      <DevBar telegram={telegram} scenario={scenario} />
      <App />
    </>,
  );
}

if (import.meta.env.DEV_TOOLS) void startDev();
else render(realTelegram(), <App />);
