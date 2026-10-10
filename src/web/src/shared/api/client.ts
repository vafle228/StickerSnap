import createClient, { type Middleware } from "openapi-fetch";
import { serverConnection } from "../platform/connection";
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

function isProblem(body: unknown): body is Problem {
  return typeof body === "object" && body !== null && typeof (body as Problem).code === "string";
}

const connection = serverConnection();

const auth: Middleware = {
  onRequest({ request }) {
    if (connection.token) request.headers.set("Authorization", `Bearer ${connection.token}`);
    return request;
  },
};

/** Typed client of the contract: paths, params, bodies and responses all come from schema.d.ts. */
export const api = createClient<paths>({ baseUrl: connection.baseUrl });
api.use(auth);

/** The response body, or an ApiError: `await call(api.GET("/api/v1/settings"))`. */
export async function call<D, E>(
  request: Promise<{ data?: D; error?: E; response: Response }>,
): Promise<D> {
  const { data, error, response } = await request;
  if (!response.ok) throw new ApiError(response.status, isProblem(error) ? error : undefined);
  return data as D;
}
