import type { PackNameCheck, Settings } from "../shared/api/types";
import { app, packSummary, summaryOf, type MockDb } from "./data";
import { fakeHub } from "./fakeHub";
import { frameSprite } from "./frames";
import { cancelJob, startJob } from "./jobs";
import { createProject, estimate, file, upload, validate } from "./media";
import { noContent, problem, route, webp } from "./route";

const BOT_TAIL = `_by_${app.botUsername}`;

/** Handlers for the requests of the contract that demo data can answer. Video streams and
 *  rendered files stay unanswered: there is no video to serve without the server. */
export function handlers(db: MockDb) {
  const findProject = (id: string) => db.projects.find((p) => p.id === id);
  const findPack = (name: string) => db.packs.find((p) => p.name === name);
  const offline = db.scenario === "offline";
  const broken = db.scenario === "project-errors";
  // «Ошибки проекта»: this one is damaged, so opening it fails with «Подробности»
  const DAMAGED = "6f1d2c3a-1b2c-4d5e-8f90-0a1b2c3d4e52";

  return [
    // ---------------------------------------------------------------- Приложение
    route("get", "/api/v1/app", () => ({
      status: 200,
      // «Десктоп»: the server says it runs as the desktop app (folder buttons in Settings)
      body: db.scenario === "desktop" ? { ...app, mode: "desktop" as const } : app,
    })),
    route("get", "/api/v1/settings", () => ({ status: 200, body: db.settings })),
    route("patch", "/api/v1/settings", ({ body }) => {
      const settings: Settings = {
        ...db.settings,
        ...body,
        folders: { ...db.settings.folders, ...body.folders },
      };
      db.settings = settings;
      fakeHub.emit("settingsChanged", settings);
      return { status: 200, body: settings };
    }),

    // ------------------------------------------------------- Источники и проверка
    route("post", "/api/v1/uploads", ({ request }) => upload(db, request)),
    route("post", "/api/v1/validate", ({ body }) => validate(db, body.source)),

    // ---------------------------------------------------------------- Проекты
    route("get", "/api/v1/projects", () => ({
      status: 200,
      body: [...db.projects].sort((a, b) => b.savedAt.localeCompare(a.savedAt)).map(summaryOf),
    })),
    route("post", "/api/v1/projects", ({ body, request }) =>
      createProject(db, body.source, request),
    ),
    route("post", "/api/v1/projects/open", ({ body }) => {
      const listed = db.projects.find((p) => p.path === body.path);
      if (listed) return { status: 200, body: listed };
      // «Вернуть» after «Убрать из списка»: the file was there all along
      const forgotten = db.forgotten.find((p) => p.path === body.path);
      if (!forgotten) return problem("project_not_found");
      db.forgotten = db.forgotten.filter((p) => p !== forgotten);
      db.projects.push(forgotten);
      fakeHub.emit("projectChanged", summaryOf(forgotten));
      return { status: 200, body: forgotten };
    }),
    route("get", "/api/v1/projects/{id}", ({ params }) => {
      if (broken && params.id === DAMAGED) {
        return problem("project_invalid", {
          log: "ssproj: scene.schemaVersion 7 is newer than 1; segments[2].outFrame is not a number",
        });
      }
      const project = findProject(params.id);
      return project ? { status: 200, body: project } : problem("project_not_found");
    }),
    route("put", "/api/v1/projects/{id}/scene", ({ params, body }) => {
      const project = findProject(params.id);
      if (!project) return problem("project_not_found");
      if (body.revision !== project.revision) return problem("revision_conflict");
      project.scene = body.scene;
      project.revision += 1;
      project.savedAt = new Date().toISOString();
      fakeHub.emit("projectChanged", summaryOf(project));
      return { status: 200, body: { revision: project.revision, savedAt: project.savedAt } };
    }),
    route("patch", "/api/v1/projects/{id}", ({ params, body }) => {
      const project = findProject(params.id);
      if (!project) return problem("project_not_found");
      if (broken)
        return problem("save_failed", {
          log: "IOException: There is not enough space on the disk.",
        });
      project.name = body.name;
      const summary = summaryOf(project);
      fakeHub.emit("projectChanged", summary);
      return { status: 200, body: summary };
    }),
    route("put", "/api/v1/projects/{id}/source", ({ params }) => {
      const project = findProject(params.id);
      if (!project) return problem("project_not_found");
      project.source = { ...project.source, status: "ok" };
      fakeHub.emit("projectChanged", summaryOf(project));
      // «Исходник не найден»: the video picked instead is shorter, the last segment falls off its end
      const last = project.scene.segments.at(-1);
      const segmentsOutOfRange = db.scenario === "source-missing" && last ? [last.id] : [];
      return { status: 200, body: { project, segmentsOutOfRange } };
    }),
    route("delete", "/api/v1/projects/{id}", ({ params, url }) => {
      const project = findProject(params.id);
      if (!project) return problem("project_not_found");
      db.projects = db.projects.filter((p) => p !== project);
      if (url.searchParams.get("mode") === "forget") db.forgotten.push(project);
      fakeHub.emit("projectRemoved", { id: params.id });
      return noContent();
    }),

    // ----------------------------------------------------------- Медиа проекта
    route("get", "/api/v1/projects/{id}/cover", () => webp()),
    route("get", "/api/v1/projects/{id}/frames", ({ params, url }) => {
      const project = findProject(params.id);
      if (!project) return problem("project_not_found");
      if (project.source.status === "missing") return problem("source_not_found");
      const query = (name: string) => Number(url.searchParams.get(name) ?? NaN);
      const from = query("from");
      const count = query("count");
      const height = query("height");
      const fits = from >= 0 && count >= 1 && from + count <= project.source.frameCount;
      if (!fits || !(height >= 1)) return problem("bad_request");
      return frameSprite(project.source, from, count, height);
    }),
    route("get", "/api/v1/projects/{id}/media", ({ params }) =>
      findProject(params.id)?.source.status === "missing"
        ? problem("source_not_found")
        : problem("source_unreadable", { detail: "Demo data has no video" }),
    ),
    route("post", "/api/v1/projects/{id}/estimate", ({ params, body }) =>
      estimate(db, params.id, body.segment),
    ),
    route("get", "/api/v1/files/{fileId}", ({ params }) => file(db, params.fileId)),

    // ---------------------------------------------------------- Фоновые задачи
    route("post", "/api/v1/jobs", ({ body }) => {
      if (body.kind === "publish" && offline) return problem("network_unavailable");
      if (body.kind === "publish" && body.target.kind === "existing") {
        const pack = findPack(body.target.pack);
        if (!pack) return problem("pack_not_found");
        if (pack.count >= pack.limit) return problem("pack_full");
      }
      return { status: 202, body: startJob(db, body) };
    }),
    route("get", "/api/v1/jobs", () => ({ status: 200, body: [...db.jobs.values()].reverse() })),
    route("get", "/api/v1/jobs/{jobId}", ({ params }) => {
      const job = db.jobs.get(params.jobId);
      return job ? { status: 200, body: job } : problem("bad_request");
    }),
    route("post", "/api/v1/jobs/{jobId}/cancel", ({ params }) => {
      const job = cancelJob(db, params.jobId);
      return job ? { status: 200, body: job } : problem("bad_request");
    }),

    // --------------------------------------------------------- Telegram-аккаунт
    route("get", "/api/v1/account", () => ({ status: 200, body: db.account })),
    route("post", "/api/v1/account/link", () => {
      db.account = {
        state: "pending",
        deepLink: `https://t.me/${app.botUsername}?start=demo`,
        expiresAt: new Date(Date.now() + 10 * 60_000).toISOString(),
      };
      // The bot gets «Start» a few seconds later.
      setTimeout(() => {
        db.account = { state: "linked", user: { id: 100200300, firstName: "Дима" } };
        fakeHub.emit("accountChanged", db.account);
      }, 4000);
      return { status: 200, body: db.account };
    }),
    route("post", "/api/v1/account/check", () =>
      offline ? problem("network_unavailable") : { status: 200, body: db.account },
    ),
    route("delete", "/api/v1/account/link", () => {
      db.account = { state: "unlinked" };
      fakeHub.emit("accountChanged", db.account);
      return { status: 200, body: db.account };
    }),

    // -------------------------------------------------------------------- Паки
    route("get", "/api/v1/packs", () => ({ status: 200, body: db.packs.map(packSummary) })),
    route("get", "/api/v1/packs/{pack}", ({ params }) => {
      const pack = findPack(params.pack);
      return pack ? { status: 200, body: pack } : problem("pack_not_found");
    }),
    route("post", "/api/v1/packs/{pack}/sync", ({ params }) => {
      if (offline) return problem("network_unavailable");
      const pack = findPack(params.pack);
      if (!pack) return problem("pack_not_found");
      pack.checkedAt = new Date().toISOString();
      fakeHub.emit("packChanged", packSummary(pack));
      return { status: 200, body: pack };
    }),
    route("post", "/api/v1/packs/import", ({ body }) => {
      if (offline) return problem("network_unavailable");
      const name = body.link.split("/").pop() ?? "";
      if (!name.endsWith(BOT_TAIL)) return problem("not_our_pack");
      const pack = findPack(name);
      return pack ? { status: 200, body: pack } : problem("pack_not_found");
    }),
    route("post", "/api/v1/packs/name-check", ({ body }) => {
      if (offline) return problem("network_unavailable");
      const name = body.name ?? "kot_divan";
      const taken = Boolean(findPack(`${name}${BOT_TAIL}`));
      const check: PackNameCheck = {
        name,
        fullName: `${name}${BOT_TAIL}`,
        problems: taken ? ["taken"] : [],
        suggestion: taken ? `${name}_2` : undefined,
      };
      return { status: 200, body: check };
    }),
    route("delete", "/api/v1/packs/{pack}", ({ params }) => {
      db.packs = db.packs.filter((p) => p.name !== params.pack);
      return noContent();
    }),

    // --------------------------------------------------------------- Публикация
    route("get", "/api/v1/projects/{id}/publish-draft", ({ params }) => {
      const project = findProject(params.id);
      if (!project) return problem("project_not_found");
      return {
        status: 200,
        body: {
          account: db.account,
          segments: project.scene.segments.map((s) => ({
            segmentId: s.id,
            name: s.name,
            durationSec: (s.outFrame - s.inFrame + 1) / 30 / s.speed,
            preset: s.preset,
            publishedIn: [],
          })),
          packs: db.packs.map((p) => ({
            ...packSummary(p),
            selectable: p.count < p.limit,
            reason: p.count < p.limit ? undefined : ("full" as const),
          })),
          recentEmoji: db.settings.recentEmoji,
        },
      };
    }),
  ];
}
