import { setupWorker } from "msw/browser";
import { createDb } from "./data";
import { handlers } from "./handlers";
import { mediaHandlers } from "./media";
import type { Scenario } from "./scenarios";

/** Dev only (/?mock or /?mock=offline): the Mini App talks to MSW instead of the bot service. */
export async function startMocks(scenario: Scenario) {
  const worker = setupWorker(...handlers(createDb(scenario)), ...mediaHandlers);
  await worker.start({ onUnhandledRequest: "bypass", quiet: true });
}
