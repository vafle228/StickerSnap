import { delay, HttpResponse } from "msw";
import { detailOf, summaryOf, type MockDb } from "./data";
import { noContent, problem, route } from "./route";

/* The demo bot service. Lists come back fresh first, as the contract says. */

const LATENCY_MS = { slow: 1500, normal: 150 };

export function handlers(db: MockDb, latency = true) {
  const wait = () =>
    latency ? delay(db.scenario === "slow" ? LATENCY_MS.slow : LATENCY_MS.normal) : undefined;
  const findPack = (name: string) => db.packs.find((p) => p.name === name);
  /** A code still waiting for an answer: an answered or expired one is link_not_found */
  const pendingLink = (code: string) =>
    db.links.find((l) => l.code === code && !l.answer && Date.parse(l.expiresAt) > Date.now());
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
    route("get", "/v1/miniapp/links/{code}", async ({ request, params }) => {
      const stop = await gate(request);
      if (stop) return stop;
      const link = pendingLink(params.code);
      if (!link) return problem("link_not_found");
      const { code, name, os, platform, appVersion, requestedAt, expiresAt, linkedHere } = link;
      const question = { code, name, os, platform, appVersion, requestedAt, expiresAt, linkedHere };
      return { status: 200, body: question };
    }),
    route("post", "/v1/miniapp/links/{code}/confirm", async ({ request, params }) => {
      const stop = await gate(request);
      if (stop) return stop;
      const answered = db.links.find((l) => l.code === params.code && l.answer === "confirmed");
      // The same person pressing «Привязать» again changes nothing (contract)
      if (answered) return noContent();
      const link = pendingLink(params.code);
      if (!link) return problem("link_not_found");
      const body = (await request.json()) as { notifications?: unknown };
      // Optional: no switch keeps the account's (contract)
      if (body.notifications !== undefined && typeof body.notifications !== "boolean") {
        return problem("bad_request");
      }
      link.answer = "confirmed";
      link.notifications = body.notifications;
      if (!link.linkedHere) {
        db.devices.push({
          id: `dev-${db.devices.length + 1}`,
          name: link.name,
          os: link.os,
          linkedAt: new Date().toISOString(),
        });
      }
      return noContent();
    }),
    route("post", "/v1/miniapp/links/{code}/decline", async ({ request, params }) => {
      const stop = await gate(request);
      if (stop) return stop;
      const link = pendingLink(params.code);
      if (!link) return problem("link_not_found");
      link.answer = "declined";
      return noContent();
    }),
  ];
}
