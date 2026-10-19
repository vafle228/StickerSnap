import createClient, { type Middleware } from "openapi-fetch";
import { serverConnection } from "../platform/connection";
import { describeRequest, trail } from "../report/trail";
import type { paths } from "./schema";
import type { Problem } from "./types";

/** A failed request. `problem` is the contract's Problem Details; a 401 has no body and no problem. */
export class ApiError extends Error {
  readonly status: number;
  readonly problem?: Problem;

  constructor(status: number, problem?: Problem) {
    super(problem ? `${problem.code}: ${problem.title}` : `HTTP ${status}`);
    this.name = "ApiError";
    this.status = status;
    this.problem = problem;
  }
}

export function isProblem(body: unknown): body is Problem {
  return typeof body === "object" && body !== null && typeof (body as Problem).code === "string";
}

const connection = serverConnection();

const auth: Middleware = {
  onRequest({ request }) {
    if (connection.token) request.headers.set("Authorization", `Bearer ${connection.token}`);
    return request;
  },
};

/** Each request with its status goes on the trail a bug's report carries (artboard 19, section 4). */
const steps: Middleware = {
  onResponse({ request, response }) {
    trail.record(describeRequest(request.method, request.url, response.status));
    return response;
  },
  onError({ request }) {
    trail.record(describeRequest(request.method, request.url, "нет ответа"));
  },
};

/** Typed client of the contract: paths, params, bodies and responses all come from schema.d.ts. */
export const api = createClient<paths>({ baseUrl: connection.baseUrl });
api.use(auth, steps);

/** An address the browser loads by itself (<video>, <img>): it cannot send the Bearer header,
 *  so the token rides in the query string, like the hub's (contract, «Доступ»). */
export function mediaSrc(path: string): string {
  const url = new URL(path, connection.baseUrl);
  if (connection.token) url.searchParams.set("access_token", connection.token);
  return url.href;
}

/** A file the server rendered (GET /api/v1/files/{fileId}), fetched with the Bearer header:
 *  the exact preview plays it from memory and the browser downloads it (R9). */
export async function fetchFile(path: string, signal?: AbortSignal): Promise<Blob> {
  const response = await fetch(new URL(path, connection.baseUrl), {
    headers: connection.token ? { Authorization: `Bearer ${connection.token}` } : undefined,
    signal,
  });
  if (!response.ok) {
    const body: unknown = await response.json().catch(() => undefined);
    throw new ApiError(response.status, isProblem(body) ? body : undefined);
  }
  return response.blob();
}

/** The response body, or an ApiError: `await call(api.GET("/api/v1/settings"))`. */
export async function call<D, E>(
  request: Promise<{ data?: D; error?: E; response: Response }>,
): Promise<D> {
  const { data, error, response } = await request;
  if (!response.ok) throw new ApiError(response.status, isProblem(error) ? error : undefined);
  return data as D;
}
