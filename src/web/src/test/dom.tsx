import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup } from "@testing-library/react";
import { setupServer } from "msw/node";
import type { ReactNode } from "react";
import { afterAll, afterEach, vi } from "vitest";
import { useApp } from "../app/store";
import { createDb, type MockDb } from "../mocks/data";
import { handlers } from "../mocks/handlers";
import type { Scenario } from "../mocks/scenarios";
import { useEditor } from "../features/editor/model/store";
import { useImport } from "../features/projects/reading";
import { useToasts } from "../shared/notify";
import { overlays } from "../shared/overlays";

/* Shared setup of component tests (*.test.tsx, happy-dom): the demo server behind msw/node, a fresh
   QueryClient per render and clean global stores. Import it before the code under test. */

export const server = setupServer();
// openapi-fetch keeps the fetch it finds when the client is made, so MSW goes first.
server.listen({ onUnhandledRequest: "error" });

const initial = {
  app: useApp.getState(),
  editor: useEditor.getState(),
};

afterEach(() => {
  cleanup();
  server.resetHandlers();
  overlays.closeAll();
  useApp.setState(initial.app, true);
  useEditor.setState(initial.editor, true);
  useImport.setState({ reading: undefined });
  useToasts.setState({ toasts: [] });
  server.events.removeAllListeners();
  vi.useRealTimers();
  vi.restoreAllMocks();
});

afterAll(() => server.close());

/** The demo server of `scenario`, as `/?mock=<scenario>` shows it. */
export function useMockServer(scenario: Scenario): MockDb {
  const db = createDb(scenario);
  server.resetHandlers(...handlers(db));
  return db;
}

/** A wrapper for render/renderHook: the providers of the app, no retries. */
export function withProviders() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
  };
}
