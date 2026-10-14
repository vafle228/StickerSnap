import { http, HttpResponse } from "msw";
import type { paths } from "../shared/api/schema";
import type { ErrorCode, Problem } from "../shared/api/types";

/* MSW handlers typed by the generated schema: the path must exist in the contract, the body
   is the operation's request type, and a reply must be one of its success responses with the
   matching status. Demo data therefore cannot drift from openapi.yaml. */

type Method = "get" | "post" | "put" | "patch" | "delete";
type Op<P extends keyof paths, M extends Method> = NonNullable<paths[P][M]>;
type Responses<P extends keyof paths, M extends Method> =
  Op<P, M> extends { responses: infer R } ? R : never;
type Json<C> = C extends { content: { "application/json": infer B } } ? B : never;
type Success = 200 | 201 | 202;

/** A success reply of the operation: `{ status: 202, body: job }` for POST /api/v1/jobs. */
type Reply<P extends keyof paths, M extends Method> = {
  [S in Success & keyof Responses<P, M>]: { status: S; body: Json<Responses<P, M>[S]> };
}[Success & keyof Responses<P, M>];

type Body<P extends keyof paths, M extends Method> =
  Op<P, M> extends { requestBody: { content: { "application/json": infer B } } } ? B : undefined;
type PathParams<P extends keyof paths, M extends Method> =
  Op<P, M> extends { parameters: { path: infer X } } ? X : Record<string, never>;

export interface MockRequest<P extends keyof paths, M extends Method> {
  params: PathParams<P, M>;
  body: Body<P, M>;
  url: URL;
  /** The request itself: its multipart body, its abort signal */
  request: Request;
}

type Resolver<P extends keyof paths, M extends Method> = (
  request: MockRequest<P, M>,
) => Reply<P, M> | Response | Promise<Reply<P, M> | Response>;

export function route<P extends keyof paths, M extends Method>(
  method: M,
  path: P,
  resolve: Resolver<P, M>,
) {
  const pattern = path.replace(/\{(\w+)\}/g, ":$1");
  return http[method](pattern, async ({ request, params }) => {
    const json = request.headers.get("content-type")?.includes("application/json");
    const body = (json ? await request.clone().json() : undefined) as Body<P, M>;
    const reply = await resolve({
      params: params as PathParams<P, M>,
      body,
      url: new URL(request.url),
      request,
    });
    if (reply instanceof Response) return reply;
    return HttpResponse.json(reply.body as never, { status: reply.status });
  });
}

/** HTTP status of each code, from the table in the contract's «Ошибки». */
const STATUS: Record<ErrorCode, number> = {
  source_unreadable: 422,
  format_unsupported: 415,
  source_not_found: 404,
  project_not_found: 404,
  project_invalid: 422,
  project_too_new: 422,
  save_failed: 507,
  revision_conflict: 409,
  disk_full: 507,
  render_failed: 500,
  cannot_fit: 422,
  ffmpeg_missing: 503,
  job_not_found: 404,
  file_not_found: 404,
  validation_failed: 422,
  account_not_linked: 409,
  bot_blocked: 403,
  network_unavailable: 503,
  bot_unavailable: 503,
  pack_name_invalid: 422,
  pack_name_taken: 409,
  pack_full: 409,
  pack_not_found: 404,
  not_our_pack: 422,
  rate_limited: 429,
  telegram_error: 502,
  bad_request: 400,
  internal: 500,
};

let traces = 0;

export function problemOf(code: ErrorCode, extra: Partial<Problem> = {}): Problem {
  return {
    type: `https://stickersnap.app/problems/${code}`,
    status: STATUS[code],
    title: code.replace(/_/g, " "),
    code,
    traceId: `mock-${++traces}`,
    ...extra,
  };
}

/** A Problem Details reply. */
export function problem(code: ErrorCode, extra: Partial<Problem> = {}): Response {
  const body = problemOf(code, extra);
  return HttpResponse.json(body, {
    status: body.status,
    headers: { "Content-Type": "application/problem+json" },
  });
}

export function noContent(): Response {
  return new HttpResponse(null, { status: 204 });
}

// 1×1 WebP for covers and frame strips: the contract sends image/webp there.
const WEBP = Uint8Array.from(atob("UklGRhoAAABXRUJQVlA4TA0AAAAvAAAAEAcQERGIiP4HAA=="), (c) =>
  c.charCodeAt(0),
);

export function webp(): Response {
  return new HttpResponse(WEBP, { headers: { "Content-Type": "image/webp" } });
}
