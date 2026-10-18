import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { afterAll, afterEach, describe, expect, it, vi } from "vitest";

/* The typed client against the bot service as the network sees it: the header it sends and what
   it makes of each kind of answer, Problem Details or not. */

vi.stubGlobal("location", new URL("http://127.0.0.1:5174/"));
const server = setupServer();
// openapi-fetch keeps the fetch it finds when the client is made, so MSW goes first.
server.listen({ onUnhandledRequest: "error" });
const { ApiError, call, createApi, isUnauthorized } = await import("./client");

const PACKS = "http://127.0.0.1:5174/v1/miniapp/packs";
const LAUNCH = "query_id=AAH&user=%7B%22id%22%3A42%7D&auth_date=1&hash=abc";

async function failure(request: Promise<unknown>) {
  return request.then(
    () => undefined,
    (e: unknown) => e,
  );
}

describe("bot service client", () => {
  afterEach(() => server.resetHandlers());
  afterAll(() => {
    server.close();
    vi.unstubAllGlobals();
  });

  it("signs every request with the launch data, on the page's own origin", async () => {
    let authorization: string | null = null;
    server.use(
      http.get(PACKS, ({ request }) => {
        authorization = request.headers.get("Authorization");
        return HttpResponse.json({ packs: [] });
      }),
    );

    const body = await call(createApi(LAUNCH).GET("/v1/miniapp/packs"));

    expect(body).toEqual({ packs: [] });
    expect(authorization).toBe(`tma ${LAUNCH}`);
  });

  it("turns Problem Details into an ApiError with the contract's code", async () => {
    server.use(
      http.get(PACKS, () =>
        HttpResponse.json(
          {
            type: "https://stickersnap.app/problems/rate_limited",
            status: 429,
            title: "Telegram asks to wait",
            code: "rate_limited",
            params: { retryAfterSec: 24 },
          },
          { status: 429, headers: { "Content-Type": "application/problem+json" } },
        ),
      ),
    );

    const error = await failure(call(createApi(LAUNCH).GET("/v1/miniapp/packs")));

    expect(error).toBeInstanceOf(ApiError);
    expect((error as InstanceType<typeof ApiError>).status).toBe(429);
    expect((error as InstanceType<typeof ApiError>).code).toBe("rate_limited");
    expect((error as InstanceType<typeof ApiError>).problem?.params).toEqual({ retryAfterSec: 24 });
    expect(isUnauthorized(error)).toBe(false);
  });

  it("knows a refused signature by its 401", async () => {
    server.use(
      http.get(PACKS, () =>
        HttpResponse.json(
          { type: "x", status: 401, title: "Unauthorized", code: "unauthorized" },
          { status: 401 },
        ),
      ),
    );

    const error = await failure(call(createApi("").GET("/v1/miniapp/packs")));

    expect(isUnauthorized(error)).toBe(true);
  });

  it("keeps the status of an answer that is no Problem, from a proxy in between", async () => {
    server.use(
      http.get(PACKS, () => new HttpResponse("<html>Bad Gateway</html>", { status: 502 })),
    );

    const error = await failure(call(createApi(LAUNCH).GET("/v1/miniapp/packs")));

    expect(error).toBeInstanceOf(ApiError);
    expect((error as InstanceType<typeof ApiError>).status).toBe(502);
    expect((error as InstanceType<typeof ApiError>).code).toBeUndefined();
  });

  it("reports no answer at all as status 0", async () => {
    server.use(http.get(PACKS, () => HttpResponse.error()));

    const error = await failure(call(createApi(LAUNCH).GET("/v1/miniapp/packs")));

    expect(error).toBeInstanceOf(ApiError);
    expect((error as InstanceType<typeof ApiError>).status).toBe(0);
  });

  it("resolves a 204 of a deletion", async () => {
    server.use(
      http.delete(
        "http://127.0.0.1:5174/v1/miniapp/devices/d1",
        () => new HttpResponse(null, { status: 204 }),
      ),
    );

    await expect(
      call(
        createApi(LAUNCH).DELETE("/v1/miniapp/devices/{deviceId}", {
          params: { path: { deviceId: "d1" } },
        }),
      ),
    ).resolves.toBeUndefined();
  });
});
