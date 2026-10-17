import { MutationCache, QueryCache, QueryClient } from "@tanstack/react-query";
import { ApiError, isUnauthorized } from "../api/client";
import { signOut } from "./session";

const onError = (error: unknown) => {
  if (isUnauthorized(error)) signOut();
};

/** A 4xx will not get better by asking again; a lost connection gets one more try. */
function retry(failures: number, error: unknown): boolean {
  if (error instanceof ApiError && error.status >= 400 && error.status < 500) return false;
  return failures < 1;
}

/** `retries: false` in tests: a failure shows at once */
export function createQueryClient({ retries = true } = {}): QueryClient {
  return new QueryClient({
    queryCache: new QueryCache({ onError }),
    mutationCache: new MutationCache({ onError }),
    defaultOptions: {
      queries: { retry: retries ? retry : false, staleTime: 30_000 },
      mutations: { retry: false },
    },
  });
}
