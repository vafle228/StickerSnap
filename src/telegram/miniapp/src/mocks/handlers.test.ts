import { setupServer } from "msw/node";
import { afterAll, afterEach, describe, expect, it, vi } from "vitest";
import type { FeedbackRequest } from "../api/types";
import { createDb, type MockDb } from "./data";
import { handlers } from "./handlers";
import type { Scenario } from "./scenarios";

/* The demo bot service answers through the Mini App's own typed client, the way screens call
   it: what «?mock» shows has to behave like bot-service.yaml says the service does. */

vi.stubGlobal("location", new URL("http://127.0.0.1:5174/"));
const server = setupServer();
// openapi-fetch keeps the fetch it finds when the client is made, so MSW goes first.
server.listen({ onUnhandledRequest: "error" });
const { ApiError, call, createApi } = await import("../api/client");
const api = createApi("query_id=test&hash=test");
let db: MockDb;

function use(scenario: Scenario) {
  db = createDb(scenario);
  server.resetHandlers(...handlers(db, false));
}

async function failure(request: Promise<unknown>) {
  const error: unknown = await request.then(
    () => undefined,
    (e: unknown) => e,
  );
  return error instanceof ApiError ? (error.code ?? error.status) : undefined;
}

const packs = async () => (await call(api.GET("/v1/miniapp/packs"))).packs;
const byName = (name: string) => ({ params: { path: { name } } });

describe("demo bot service", () => {
  afterEach(() => server.resetHandlers());
  afterAll(() => {
    server.close();
    vi.unstubAllGlobals();
  });

  it("refuses a request without Telegram's signature", async () => {
    use("ok");
    const unsigned = createApi("");
    expect(await failure(call(unsigned.GET("/v1/miniapp/packs")))).toBe("unauthorized");
  });

  it("lists packs fresh first, each with its count against the limit", async () => {
    use("ok");
    const list = await packs();
    const dates = list.map((p) => p.updatedAt);
    expect(dates).toEqual([...dates].sort().reverse());
    expect(list[0]).toMatchObject({ title: "Реакции с созвона", count: 12, limit: 120 });
    expect(list.find((p) => p.kind === "emoji")?.limit).toBe(200);
  });

  it("deletes a sticker, and the last one takes the pack with it", async () => {
    use("ok");
    const name = "summer_2026_by_StickerSnapAppBot";
    const pack = await call(api.GET("/v1/miniapp/packs/{name}", byName(name)));
    for (const sticker of pack.stickers) {
      await call(
        api.DELETE("/v1/miniapp/packs/{name}/stickers/{stickerId}", {
          params: { path: { name, stickerId: sticker.id } },
        }),
      );
    }
    expect((await packs()).some((p) => p.name === name)).toBe(false);
    expect(await failure(call(api.GET("/v1/miniapp/packs/{name}", byName(name))))).toBe(
      "pack_not_found",
    );
  });

  it("forgets only a pack Telegram no longer knows", async () => {
    use("pack-missing");
    const forget = (name: string) => call(api.DELETE("/v1/miniapp/packs/{name}", byName(name)));
    expect(await failure(forget("cat_sofa_by_StickerSnapAppBot"))).toBe("pack_in_telegram");
    await forget("first_test_by_StickerSnapAppBot");
    expect((await packs()).some((p) => p.state === "missing")).toBe(false);
  });

  it("unlinks a computer once", async () => {
    use("ok");
    const unlink = () =>
      call(
        api.DELETE("/v1/miniapp/devices/{deviceId}", { params: { path: { deviceId: "dev-1" } } }),
      );
    await unlink();
    expect(await failure(unlink())).toBe("device_not_found");
  });

  it("answers a link code once: confirm or decline, then link_not_found", async () => {
    use("ok");
    const byCode = (code: string) => ({ params: { path: { code } } });
    const link = await call(api.GET("/v1/miniapp/links/{code}", byCode("demo")));
    expect(link).toMatchObject({ name: "DESKTOP-7F3K2Q", os: "windows", linkedHere: false });
    const confirm = () =>
      call(
        api.POST("/v1/miniapp/links/{code}/confirm", {
          ...byCode("demo"),
          body: { notifications: true },
        }),
      );
    await confirm();
    // The same person again: nothing changes (contract)
    await confirm();
    expect(db.devices.filter((d) => d.name === "DESKTOP-7F3K2Q")).toHaveLength(2);
    expect(await failure(call(api.GET("/v1/miniapp/links/{code}", byCode("demo"))))).toBe(
      "link_not_found",
    );
    expect(await failure(call(api.POST("/v1/miniapp/links/{code}/decline", byCode("demo"))))).toBe(
      "link_not_found",
    );
    expect(await failure(call(api.GET("/v1/miniapp/links/{code}", byCode("nope"))))).toBe(
      "link_not_found",
    );
  });

  it("takes feedback by the contract's rules, five an hour", async () => {
    use("ok");
    const send = (body: FeedbackRequest) =>
      call(api.POST("/v1/feedback", { body }));
    expect(await send({ type: "bug", text: "Пак пустой", canReply: true })).toEqual({ id: 41 });
    expect(await failure(send({ type: "idea", text: "   ", canReply: false }))).toBe(
      "bad_request",
    );
    // the Mini App's caller is known: a contact is the desktop's, for a computer without a link
    expect(
      await failure(send({ type: "other", text: "Привет", canReply: true, contact: "@me" })),
    ).toBe("bad_request");
    for (let i = 0; i < 4; i += 1) await send({ type: "idea", text: "Ещё", canReply: false });
    expect(await failure(send({ type: "idea", text: "Шестой", canReply: false }))).toBe(
      "rate_limited",
    );
    expect(db.feedback).toHaveLength(5);

    use("feedback-limit");
    expect(await failure(send({ type: "idea", text: "Первый", canReply: false }))).toBe(
      "rate_limited",
    );
  });

  it("fails without an answer in «Нет связи», and with telegram_error in «Не удаляется»", async () => {
    use("offline");
    expect(await failure(packs())).toBe(0);
    use("delete-fails");
    const remove = api.DELETE("/v1/miniapp/packs/{name}/stickers/{stickerId}", {
      params: { path: { name: "cat_sofa_by_StickerSnapAppBot", stickerId: "x" } },
    });
    expect(await failure(call(remove))).toBe("telegram_error");
  });
});
