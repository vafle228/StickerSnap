import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState, type ReactNode } from "react";
import { ApiError } from "../shared/api/client";

/** A 4xx answer is final; a 5xx or a dropped connection gets two more tries. */
function retry(failures: number, error: Error) {
  if (error instanceof ApiError && error.status < 500) return false;
  return failures < 2;
}

export function Providers({ children }: { children: ReactNode }) {
  const [queryClient] = useState(() => new QueryClient({ defaultOptions: { queries: { retry } } }));
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}
