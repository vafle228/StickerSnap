import { setupWorker } from "msw/browser";
import { setHubFactory } from "../shared/api/hub";
import { createDb } from "./data";
import { createFakeHub } from "./fakeHub";
import { handlers } from "./handlers";
import { setRenderer } from "./jobs";
import { availableRenderer } from "./render";
import type { Scenario } from "./scenarios";

/** Dev only (/?mock or /?mock=offline): the SPA talks to MSW and to a fake hub instead of the server. */
export async function startMocks(scenario: Scenario) {
  setHubFactory(createFakeHub);
  setRenderer(availableRenderer());
  const worker = setupWorker(...handlers(createDb(scenario)));
  await worker.start({ onUnhandledRequest: "bypass", quiet: true });
}
