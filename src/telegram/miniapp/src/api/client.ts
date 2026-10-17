import createClient, { type Client } from "openapi-fetch";
import type { paths } from "./schema";
import type { ErrorCode, Problem } from "./types";

/** A failed request: the contract's Problem Details, or none when the service did not answer. */
export class ApiError extends Error {
  readonly status: number;
  readonly problem?: Problem;

  constructor(status: number, problem?: Problem) {
    super(problem ? `${problem.code}: ${problem.title}` : `HTTP ${status}`);
    this.name = "ApiError";
    this.status = status;
    this.problem = problem;
  }

  get code(): ErrorCode | undefined {
    return this.problem?.code;
  }
}

function isProblem(body: unknown): body is Problem {
  return typeof body === "object" && body !== null && typeof (body as Problem).code === "string";
}

/** Telegram's signature did not pass: «Не получилось войти» (artboard 14, state 4). */
export function isUnauthorized(error: unknown): boolean {
  return error instanceof ApiError && error.status === 401;
}

export type Api = Client<paths>;

/** The typed client of src/contract/bot-service.yaml. Every request carries the launch data
 *  Telegram signed (`Authorization: tma <initData>`); the service is the page's own origin. */
export function createApi(initData: string, baseUrl = location.origin): Api {
  const api = createClient<paths>({ baseUrl });
  api.use({
    onRequest({ request }) {
      request.headers.set("Authorization", `tma ${initData}`);
      return request;
    },
  });
  return api;
}

/** The response body, or an ApiError: `await call(api.GET("/v1/miniapp/packs"))`. */
export async function call<D, E>(
  request: Promise<{ data?: D; error?: E; response: Response }>,
): Promise<D> {
  let result: { data?: D; error?: E; response: Response };
  try {
    result = await request;
  } catch {
    // No answer at all: the service is down or the phone is offline
    throw new ApiError(0);
  }
  const { data, error, response } = result;
  if (!response.ok) throw new ApiError(response.status, isProblem(error) ? error : undefined);
  return data as D;
}
