import { checkLink, type LinkKind } from "../features/projects/link/parse";
import type { Download, LinkVideo, Problem } from "../shared/api/types";
import { summaryOf, type MockDb } from "./data";
import { fakeHub } from "./fakeHub";
import { newProject } from "./media";
import { problem, problemOf } from "./route";

/* Import by link of the demo server (artboard 20): no site is asked, the video is made up from
   the look of the link, and the download is a timer. The dev bar's «Ссылка: …» scenarios give
   the failures of section 4; a playlist or a live link gives its own hint by its look. */

const MB = 1024 * 1024;
/** «Узнаю о видео…» lasts long enough to see its card (shown after 300 ms) */
const PROBE_MS = 900;
const TICK_MS = 250;
/** Reading the downloaded file, as «Читаю файл» */
const READ_MS = 1500;

/** A preview the demo can show without a site: the site's colour with a play mark. */
function thumbnail(from: string, to: string): string {
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 160 90"><defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${from}"/><stop offset="1" stop-color="${to}"/></linearGradient></defs><rect width="160" height="90" fill="url(#g)"/><path d="M70 30v30l26-15z" fill="#fff" opacity=".9"/></svg>`;
  return `data:image/svg+xml,${encodeURIComponent(svg)}`;
}

function videoOf(db: MockDb, url: string, kind: LinkKind): LinkVideo {
  const clean = url.trim().replace(/[?&](t|list|index)=[^&]*/g, "");
  if (kind === "giphy-sticker") {
    return {
      site: "giphy",
      url: clean,
      title: "Танцующий кот",
      durationSec: 2.4,
      thumbnailUrl: thumbnail("#ffd6e8", "#c8b6ff"),
      bytes: Math.round(0.9 * MB),
      quality: "WEBP",
    };
  }
  if (kind === "giphy-gif") {
    return {
      site: "giphy",
      url: clean,
      title: "Кот печатает на клавиатуре",
      durationSec: 3.2,
      thumbnailUrl: thumbnail("#9ae6ff", "#b388ff"),
      bytes: Math.round(1.4 * MB),
      quality: "MP4",
    };
  }
  if (db.scenario === "link-long") {
    return {
      site: "youtube",
      url: clean,
      title: "Стрим: лучшие моменты недели",
      durationSec: 4360,
      thumbnailUrl: thumbnail("#ff8a80", "#ffd180"),
      bytes: 410 * MB,
      quality: "720p",
    };
  }
  return {
    site: "youtube",
    url: clean,
    title: "Кот смотрит в окно",
    durationSec: 48,
    thumbnailUrl: thumbnail("#ff8a80", "#ffd180"),
    bytes: 38 * MB,
    quality: "720p",
  };
}

/** What the server would say of the link before downloading, or the problem with it. */
function look(db: MockDb, url: string): { video: LinkVideo } | { problem: Problem } {
  const check = checkLink(url);
  if (!check.ok) {
    return { problem: problemOf("link_unsupported", { params: { reason: check.reason } }) };
  }
  if (db.scenario === "offline") {
    return { problem: problemOf("site_unreachable", { params: { site: check.site } }) };
  }
  const path = url.toLowerCase();
  if (path.includes("/playlist")) {
    return { problem: problemOf("video_unavailable", { params: { reason: "playlist" } }) };
  }
  if (path.includes("/live/")) {
    return { problem: problemOf("video_unavailable", { params: { reason: "live" } }) };
  }
  if (db.scenario === "link-gone") {
    return { problem: problemOf("video_unavailable", { params: { reason: "gone" } }) };
  }
  return { video: videoOf(db, url, check.kind) };
}

const wait = (ms: number, signal: AbortSignal) =>
  new Promise<void>((resolve) => {
    const timer = setTimeout(resolve, ms);
    signal.addEventListener("abort", () => {
      clearTimeout(timer);
      resolve();
    });
  });

const reply = (p: Problem) => problem(p.code, p);

/** POST /api/v1/links/probe: dropping the request is «Отменить» of «Узнаю о видео…». */
export async function probeLink(db: MockDb, url: string, request: Request) {
  await wait(PROBE_MS, request.signal);
  if (request.signal.aborted) return problem("bad_request");
  const found = look(db, url);
  return "problem" in found ? reply(found.problem) : { status: 200 as const, body: found.video };
}

const timers = new Map<string, ReturnType<typeof setInterval>>();

function update(db: MockDb, id: string, patch: Partial<Download>): Download | undefined {
  const index = db.downloads.findIndex((d) => d.id === id);
  const before = db.downloads[index];
  if (!before) return undefined;
  const after = { ...before, ...patch };
  db.downloads[index] = after;
  fakeHub.emit("downloadUpdated", after);
  return after;
}

const isOn = (d: Download) =>
  d.state === "queued" || d.state === "downloading" || d.state === "reading";

/** The demo's speed: a short clip in about 8 s, a long one in a minute, so both can be watched. */
const speedOf = (video: LinkVideo) =>
  video.durationSec > 600 ? video.bytes / 60 : Math.max(video.bytes / 8, 0.4 * MB);

function stop(id: string) {
  clearInterval(timers.get(id));
  timers.delete(id);
}

/** Runs the oldest queued download, one at a time: the second waits for the first. */
function next(db: MockDb) {
  if (db.downloads.some((d) => d.state === "downloading" || d.state === "reading")) return;
  const queued = [...db.downloads].reverse().find((d) => d.state === "queued");
  if (queued) run(db, queued.id);
}

function fail(db: MockDb, id: string, error: Problem) {
  stop(id);
  update(db, id, { state: "failed", error, etaSec: undefined });
  next(db);
}

function run(db: MockDb, id: string) {
  const start = db.downloads.find((d) => d.id === id);
  if (!start) return;
  const { video } = start;
  // The room is checked before a byte comes: «Не хватает места для ролика»
  if (db.scenario === "link-disk") {
    const freeBytes = 12 * MB;
    fail(
      db,
      id,
      problemOf("disk_full", {
        params: { needBytes: video.bytes, freeBytes },
        log: `Need ${video.bytes} bytes for «${video.title}», ${freeBytes} free on C:`,
      }),
    );
    return;
  }
  const speed = speedOf(video);
  update(db, id, { state: "downloading", etaSec: video.bytes / speed });
  timers.set(
    id,
    setInterval(() => {
      const now = db.downloads.find((d) => d.id === id);
      if (!now || now.state !== "downloading") return;
      const receivedBytes = Math.min(video.bytes, now.receivedBytes + (speed * TICK_MS) / 1000);
      if (db.scenario === "link-broken" && receivedBytes > video.bytes * 0.3) {
        fail(
          db,
          id,
          problemOf("site_changed", {
            params: { site: video.site },
            log: `ERROR: [${video.site}] ${video.url}: Unable to extract player response; please report this issue`,
          }),
        );
        return;
      }
      if (receivedBytes < video.bytes) {
        update(db, id, { receivedBytes, etaSec: (video.bytes - receivedBytes) / speed });
        return;
      }
      stop(id);
      update(db, id, { receivedBytes, state: "reading", etaSec: undefined });
      setTimeout(() => {
        if (db.downloads.find((d) => d.id === id)?.state !== "reading") return;
        const project = newProject(db, `${video.title}.mp4`, video.bytes);
        db.projects.unshift(project);
        fakeHub.emit("projectChanged", summaryOf(project));
        update(db, id, { state: "succeeded", projectId: project.id });
        next(db);
      }, READ_MS);
    }, TICK_MS),
  );
}

/** POST /api/v1/downloads: 202 and the download; downloadUpdated brings the rest. */
export function createDownload(db: MockDb, url: string) {
  const found = look(db, url);
  if ("problem" in found) return reply(found.problem);
  const download: Download = {
    id: crypto.randomUUID(),
    video: found.video,
    state: "queued",
    receivedBytes: 0,
    createdAt: new Date().toISOString(),
  };
  db.downloads.unshift(download);
  // The answer goes out first: the hub's events follow it, as on the server
  setTimeout(() => next(db), 0);
  return { status: 202 as const, body: download };
}

/** POST /api/v1/downloads/{id}/cancel: the partial file is gone, the next in line starts. */
export function cancelDownload(db: MockDb, id: string) {
  const download = db.downloads.find((d) => d.id === id);
  if (!download) return problem("job_not_found");
  if (!isOn(download)) return { status: 200 as const, body: download };
  stop(id);
  const canceled = update(db, id, { state: "canceled", etaSec: undefined }) ?? download;
  next(db);
  return { status: 200 as const, body: canceled };
}
