import { setupWorker } from "msw/browser";
import { setHubFactory } from "../shared/api/hub";
import { platform } from "../shared/platform";
import { crashProbe } from "../shared/report/probe";
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
  // The errors of artboard 19 the demo server cannot cause: a section that throws, a last crash
  if (scenario === "screen-crash") crashProbe.armed = true;
  if (scenario === "last-crash") {
    let crash: Awaited<ReturnType<typeof platform.lastCrash>> = {
      kind: "server",
      at: new Date(Date.now() - 3_600_000).toISOString(),
      code: 3,
      output: "Unhandled exception. System.IO.IOException: The disk is full.",
    };
    platform.lastCrash = () => {
      const taken = crash;
      crash = null;
      return Promise.resolve(taken);
    };
  }
}
