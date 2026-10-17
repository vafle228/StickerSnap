import { cleanup, render } from "@testing-library/react";
import { setupServer } from "msw/node";
import { afterAll, afterEach } from "vitest";
import { App } from "../app/App";
import { boot } from "../app/boot";
import { useNav } from "../app/nav";
import { Providers } from "../app/Providers";
import { createQueryClient } from "../app/queryClient";
import { useSession } from "../app/session";
import { createDb, type MockDb } from "../mocks/data";
import { handlers } from "../mocks/handlers";
import type { Scenario } from "../mocks/scenarios";
import { useToast } from "../shared/toast";
import { createFakeTelegram, type FakeTelegramOptions } from "../telegram/fakeTelegram";

/* Shared setup of component tests (*.test.tsx, happy-dom): the demo bot service behind
   msw/node, a fake Telegram, a fresh QueryClient per render and clean global stores.
   Import it before the code under test. */

export const server = setupServer();
// openapi-fetch keeps the fetch it finds when the client is made, so MSW goes first.
server.listen({ onUnhandledRequest: "error" });

afterEach(() => {
  cleanup();
  server.resetHandlers();
  useNav.getState().reset([{ kind: "packs" }]);
  useSession.setState({ signedOut: false });
  useToast.setState({ current: undefined });
});

afterAll(() => server.close());

export interface Launch extends FakeTelegramOptions {
  scenario?: Scenario;
}

/** The whole Mini App on the demo data of a scenario, as Telegram launches it. */
export function launch({
  scenario = "ok",
  initData = "query_id=test&hash=test",
  ...rest
}: Launch = {}) {
  const db: MockDb = createDb(scenario);
  server.resetHandlers(...handlers(db, false));
  const telegram = createFakeTelegram({ initData, ...rest });
  const client = createQueryClient({ retries: false });
  boot(telegram);
  const view = render(
    <Providers telegram={telegram} client={client}>
      <App />
    </Providers>,
  );
  return { ...view, db, telegram };
}
