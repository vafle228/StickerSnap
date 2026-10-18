import { http, HttpResponse } from "msw";
import type { paths } from "../api/schema";
import type { ErrorCode, Problem } from "../api/types";

/* MSW handlers typed by the generated schema, as in src/web: the path must exist in
   bot-service.yaml and a reply must be one of the operation's success responses. The demo
   data therefore cannot drift from the contract. */

type Method = "get" | "delete";
type Op<P extends keyof paths, M extends Method> = NonNullable<paths[P][M]>;
type Responses<P extends keyof paths, M extends Method> =
  Op<P, M> extends { responses: infer R } ? R : never;
type Json<C> = C extends { content: { "application/json": infer B } } ? B : never;
type Success = 200;

type Reply<P extends keyof paths, M extends Method> = {
  [S in Success & keyof Responses<P, M>]: { status: S; body: Json<Responses<P, M>[S]> };
}[Success & keyof Responses<P, M>];

type PathParams<P extends keyof paths, M extends Method> =
  Op<P, M> extends { parameters: { path: infer X } } ? X : Record<string, never>;

type Resolver<P extends keyof paths, M extends Method> = (request: {
  params: PathParams<P, M>;
  request: Request;
}) => Reply<P, M> | Response | Promise<Reply<P, M> | Response>;

export function route<P extends keyof paths, M extends Method>(
  method: M,
  path: P,
  resolve: Resolver<P, M>,
) {
  const pattern = path.replace(/\{(\w+)\}/g, ":$1");
  return http[method](pattern, async ({ request, params }) => {
    const reply = await resolve({ params: params as PathParams<P, M>, request });
    if (reply instanceof Response) return reply;
    return HttpResponse.json(reply.body as never, { status: reply.status });
  });
}

/** HTTP status of each code, from the table of ErrorCode in the contract. */
const STATUS: Record<ErrorCode, number> = {
  bad_request: 400,
  unauthorized: 401,
  pack_not_found: 404,
  sticker_not_found: 404,
  device_not_found: 404,
  link_not_found: 404,
  pack_in_telegram: 409,
  rate_limited: 429,
  internal: 500,
  telegram_error: 502,
};

export function problemOf(code: ErrorCode): Problem {
  return {
    type: `https://stickersnap.app/problems/${code}`,
    status: STATUS[code],
    title: code.replace(/_/g, " "),
    code,
  };
}

export function problem(code: ErrorCode): Response {
  const body = problemOf(code);
  return HttpResponse.json(body, {
    status: body.status,
    headers: { "Content-Type": "application/problem+json" },
  });
}

export function noContent(): Response {
  return new HttpResponse(null, { status: 204 });
}
