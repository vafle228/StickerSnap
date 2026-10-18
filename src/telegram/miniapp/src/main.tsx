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

// Dev tools, dropped from the production build (vite.config.ts). /?mock runs the Mini App in a
// plain browser on demo data (/?mock=offline and the other scenarios of mocks/scenarios.ts);
// /?tma=<launch data> runs it against the real bot service that Vite proxies /v1 to, with launch
// data the service signed (`uv run stickersnap-bot-launch-data <user id>` in src/telegram/bot).
// Both get a dev bar that plays Telegram's header and switches the theme. &write=ask or
// &write=deny launches as someone the bot may not write to yet, who answers Telegram's
// «Разрешить боту писать?» with «yes», or with «no» first.
async function startDev() {
  const params = new URLSearchParams(location.search);
  const launchData = params.get("tma");
  if (!params.has("mock") && launchData === null) {
    render(realTelegram(), <App />);
    return;
  }
  const [{ isScenario }, { DevBar }] = await Promise.all([
    import("./mocks/scenarios"),
    import("./dev/DevBar"),
  ]);
  const asked = params.get("mock");
  const scenario = launchData !== null ? undefined : isScenario(asked) ? asked : "ok";
  if (scenario) await (await import("./mocks/start")).startMocks(scenario);
  const telegram = createFakeTelegram({
    initData: launchData ?? "query_id=mock&user=%7B%22id%22%3A1%7D&hash=mock",
    startParam: params.get("startapp") ?? undefined,
    colorScheme: params.get("theme") === "dark" ? "dark" : "light",
    ...(params.has("write") && {
      canWrite: false,
      writeAnswers: params.get("write") === "deny" ? [false, true] : [true],
    }),
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
