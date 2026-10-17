import { useMemo, useState, type ReactNode } from "react";
import { QueryClientProvider, type QueryClient } from "@tanstack/react-query";
import { createApi } from "../api/client";
import { ApiContext } from "../api/queries";
import type { Telegram } from "../telegram/bridge";
import { TelegramContext } from "../telegram/context";
import { createQueryClient } from "./queryClient";

export function Providers({
  telegram,
  client: given,
  children,
}: {
  telegram: Telegram;
  /** Tests pass their own, without retries */
  client?: QueryClient;
  children: ReactNode;
}) {
  const [client] = useState(() => given ?? createQueryClient());
  const api = useMemo(() => createApi(telegram.initData), [telegram]);
  return (
    <TelegramContext.Provider value={telegram}>
      <ApiContext.Provider value={api}>
        <QueryClientProvider client={client}>{children}</QueryClientProvider>
      </ApiContext.Provider>
    </TelegramContext.Provider>
  );
}
