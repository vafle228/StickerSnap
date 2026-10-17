import { delay, HttpResponse } from "msw";
import { detailOf, summaryOf, type MockDb } from "./data";
import { noContent, problem, route } from "./route";

/* The demo bot service. Lists come back fresh first, as the contract says. */

const LATENCY_MS = { slow: 1500, normal: 150 };

export function handlers(db: MockDb, latency = true) {
  const wait = () =>
    latency ? delay(db.scenario === "slow" ? LATENCY_MS.slow : LATENCY_MS.normal) : undefined;
  const findPack = (name: string) => db.packs.find((p) => p.name === name);
  /** Whether the request goes through at all: «Нет связи» and «Не вошли» stop it here */
  const gate = async (request: Request): Promise<Response | undefined> => {
    await wait();
    if (db.scenario === "offline") return HttpResponse.error();
    const auth = request.headers.get("Authorization") ?? "";
    if (db.scenario === "unauthorized" || !auth.startsWith("tma ") || auth === "tma ") {
      return problem("unauthorized");
    }
    return undefined;
  };

  return [
    route("get", "/v1/miniapp/packs", async ({ request }) => {
      const stop = await gate(request);
      if (stop) return stop;
      const packs = [...db.packs]
        .sort((a, b) => b.updatedAt.localeCompare(a.updatedAt))
        .map(summaryOf);
      return { status: 200, body: { packs } };
    }),
    route("get", "/v1/miniapp/packs/{name}", async ({ request, params }) => {
      const stop = await gate(request);
      if (stop) return stop;
      const pack = findPack(params.name);
      return pack ? { status: 200, body: detailOf(pack) } : problem("pack_not_found");
    }),
    route("delete", "/v1/miniapp/packs/{name}", async ({ request, params }) => {
      const stop = await gate(request);
      if (stop) return stop;
      const pack = findPack(params.name);
      if (!pack) return problem("pack_not_found");
      if (pack.state !== "missing") return problem("pack_in_telegram");
      db.packs = db.packs.filter((p) => p !== pack);
      return noContent();
    }),
    route(
      "delete",
      "/v1/miniapp/packs/{name}/stickers/{stickerId}",
      async ({ request, params }) => {
        const stop = await gate(request);
        if (stop) return stop;
        if (db.scenario === "delete-fails") return problem("telegram_error");
        const pack = findPack(params.name);
        if (!pack) return problem("pack_not_found");
        const left = pack.stickers.filter((s) => s.id !== params.stickerId);
        if (left.length === pack.stickers.length) return problem("sticker_not_found");
        pack.stickers = left;
        pack.coverUrl = left[0]?.thumbUrl;
        // The last sticker takes the pack with it: Telegram drops an empty set
        if (left.length === 0) db.packs = db.packs.filter((p) => p !== pack);
        return noContent();
      },
    ),
    route("get", "/v1/miniapp/devices", async ({ request }) => {
      const stop = await gate(request);
      if (stop) return stop;
      const devices = [...db.devices].sort((a, b) => b.linkedAt.localeCompare(a.linkedAt));
      return { status: 200, body: { devices } };
    }),
    route("delete", "/v1/miniapp/devices/{deviceId}", async ({ request, params }) => {
      const stop = await gate(request);
      if (stop) return stop;
      const before = db.devices.length;
      db.devices = db.devices.filter((d) => d.id !== params.deviceId);
      return db.devices.length < before ? noContent() : problem("device_not_found");
    }),
  ];
}
