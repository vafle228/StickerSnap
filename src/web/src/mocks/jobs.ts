import type { ExportResult, Job, JobItem, JobRequest, Project, Segment } from "../shared/api/types";
import { app, packSummary, type MockDb } from "./data";
import {
  bytesPerFrame,
  climb,
  ladder,
  ladderSteps,
  outputOf,
  sourceFps,
  stickerDuration,
  type Rung,
} from "./encoder";
import { fakeHub } from "./fakeHub";
import type { Renderer } from "./render";
import { problemOf } from "./route";
import { runsAsDesktop } from "./scenarios";
import { validationOf } from "./sources";

/* Background jobs played by timer: progress through the stages, then the result, each step
   sent as jobUpdated with the whole Job (contract, «Фоновые задачи и события»). A preview and
   an export walk the encoder's ladder (encoder.ts) one attempt after another; with a renderer
   (the browser) the last attempt is drawn into a real .webm, served by GET /api/v1/files. */

const TICK_MS = 300;
const STEPS = 10;
const timers = new Map<string, ReturnType<typeof setInterval>>();

let renderer: Renderer | undefined;

/** The browser's renderer; unit tests leave it out and get the numbers without the file. */
export function setRenderer(render: Renderer | undefined) {
  renderer = render;
}

function put(db: MockDb, job: Job) {
  db.jobs.set(job.id, job);
  fakeHub.emit("jobUpdated", job);
}

function stop(id: string) {
  clearInterval(timers.get(id));
  timers.delete(id);
}

function itemsOf(request: JobRequest): JobItem[] {
  const ids =
    request.kind === "preview"
      ? [request.segment.id]
      : request.kind === "export"
        ? request.segmentIds
        : request.items.map((i) => i.segmentId);
  return ids.map((segmentId) => ({ segmentId, state: "queued", fraction: 0 }));
}

/* ------------------------------------------------------------- Preview and export */

/** One tick of the plan: what the job reports while it lasts. */
interface Tick {
  item: number;
  stage: Job["progress"]["stage"];
  /** The item's own progress at this tick */
  fraction: number;
  attempt?: Rung;
  attemptN?: number;
}

interface ItemPlan {
  segment: Segment;
  tried: Rung[];
  fits: boolean;
  /** The file of the last attempt, once drawn */
  file?: Promise<Blob | undefined>;
}

function planItem(db: MockDb, project: Project, segment: Segment, preview: boolean): ItemPlan {
  const limits = app.limits[segment.preset];
  const rungs = ladder(project.source, segment, limits, bytesPerFrame(db.scenario));
  // Fitting switched off: the first rung as it is, over the limit or not.
  if (!segment.autoFit) return { segment, tried: rungs.slice(0, 1), fits: true };
  const { tried, fits } = climb(rungs, limits.maxBytes);
  // A draft is drawn once, at the rung that will fit: it shows what the export will give.
  return { segment, tried: preview ? tried.slice(-1) : tried, fits: preview || fits };
}

function ticksOf(plans: ItemPlan[], preview: boolean): Tick[] {
  const ticks: Tick[] = [];
  plans.forEach((plan, item) => {
    ticks.push({ item, stage: "probe", fraction: 0.05 });
    const share = 0.9 / plan.tried.length;
    plan.tried.forEach((rung, n) => {
      const base = 0.05 + n * share;
      const attempt = { attempt: rung, attemptN: n + 1 };
      if (!preview) {
        ticks.push({ item, stage: "pass1", fraction: base + share * 0.2, ...attempt });
        ticks.push({ item, stage: "pass1", fraction: base + share * 0.4, ...attempt });
      }
      ticks.push({ item, stage: "pass2", fraction: base + share * 0.6, ...attempt });
      ticks.push({ item, stage: "pass2", fraction: base + share * 0.8, ...attempt });
      ticks.push({ item, stage: "pass2", fraction: base + share, ...attempt });
    });
    if (!preview) ticks.push({ item, stage: "validate", fraction: 1 });
  });
  return ticks;
}

/** Draws the last attempt of each item, one after another, while the ticks run. */
function drawFiles(project: Project, plans: ItemPlan[]) {
  let previous: Promise<unknown> = Promise.resolve();
  for (const plan of plans) {
    const rung = plan.tried.at(-1);
    if (!renderer || !rung || !plan.fits) continue;
    const render = renderer;
    const duration = stickerDuration(plan.segment, sourceFps(project.source));
    const task = {
      source: project.source,
      segment: plan.segment,
      fps: rung.fps,
      side: rung.side,
      // About the forecast's size; when fitting, under the limit with room for the container
      bitsPerSecond: Math.round(
        ((plan.segment.autoFit ? Math.min(rung.bytes, 248_000) : rung.bytes) * 8 * 0.92) / duration,
      ),
    };
    const file = previous.then(() => render(task, () => {}).catch(() => undefined));
    previous = file;
    plan.file = file;
  }
}

function exportResult(
  db: MockDb,
  project: Project,
  plan: ItemPlan,
  revision: number,
  blob: Blob | undefined,
): ExportResult {
  const rung = plan.tried.at(-1) as Rung;
  const limits = app.limits[plan.segment.preset];
  // The encoder model's size, so the ladder, the result and the forecast agree; the demo file
  // itself is a recording and weighs what MediaRecorder made of it.
  const bytes = rung.bytes;
  const fileId = crypto.randomUUID();
  if (blob) db.files.set(fileId, blob);
  const output = {
    ...outputOf(plan.segment, rung.side),
    fps: rung.fps,
    durationSec: stickerDuration(plan.segment, sourceFps(project.source)),
  };
  const fileName = `${plan.segment.name}.webm`;
  const steps = ladderSteps(plan.tried);
  return {
    segmentId: plan.segment.id,
    sceneRevision: revision,
    stale: false,
    path: runsAsDesktop(db.scenario) ? `${db.settings.folders.stickers}/${fileName}` : undefined,
    fileUrl: `/api/v1/files/${fileId}`,
    bytes,
    output,
    crf: rung.crf,
    concessions: rung.concessions,
    ladder: steps,
    validation: validationOf(
      {
        fileName,
        bytes,
        container: "webm",
        codec: "vp9",
        width: output.width,
        height: output.height,
        durationSec: output.durationSec,
        // A long sticker (R19) declares 2.9 s in its header, as the server writes it
        declaredDurationSec: output.durationSec > limits.maxDurationSec + 1e-9 ? 2.9 : undefined,
        fps: output.fps,
        audio: "none",
        alpha: project.source.hasAlpha ? "present" : "none",
      },
      limits,
      plan.segment.preset === "emoji",
    ),
  };
}

function finishedRender(
  db: MockDb,
  project: Project,
  job: Job,
  plans: ItemPlan[],
  blobs: Map<number, Blob | undefined>,
  revision: number,
): Job {
  const failed = plans.findIndex((p) => !p.fits);
  if (failed >= 0) {
    const error = problemOf("cannot_fit", {
      log: `smallest attempt ${plans[failed]?.tried.at(-1)?.bytes ?? 0} B > ${app.limits.sticker.maxBytes} B`,
    });
    return {
      ...job,
      state: "failed",
      error,
      items: job.items.map((it, i) => (i === failed ? { ...it, state: "failed", error } : it)),
    };
  }
  const items = job.items.map((item, i): JobItem => {
    const plan = plans[i];
    if (!plan) return { ...item, state: "done", fraction: 1 };
    const result = exportResult(db, project, plan, revision, blobs.get(i));
    if (job.kind === "preview") {
      return {
        ...item,
        state: "done",
        fraction: 1,
        preview: { fileUrl: result.fileUrl, bytes: result.bytes },
      };
    }
    project.exports[plan.segment.id] = result;
    return { ...item, state: "done", fraction: 1, export: result };
  });
  return { ...job, state: "succeeded", progress: { fraction: 1, stage: "validate" }, items };
}

function startRender(db: MockDb, job: Job, request: JobRequest & { kind: "preview" | "export" }) {
  const project = db.projects.find((p) => p.id === request.projectId);
  if (!project) return;
  const preview = request.kind === "preview";
  const segments = preview
    ? [request.segment]
    : request.segmentIds.flatMap((id) => project.scene.segments.filter((s) => s.id === id));
  const plans = segments.map((s) => planItem(db, project, s, preview));
  const ticks = ticksOf(plans, preview);
  drawFiles(project, plans);
  const revision = preview ? project.revision : request.revision;
  // «Экспорт не удался»: the disk fills up during the first pass
  const failAt = db.scenario === "export-fails" && !preview ? 3 : -1;
  const blobs = new Map<number, Blob | undefined>();
  let step = 0;
  let waiting = false;

  const tick = () => {
    const current = db.jobs.get(job.id);
    if (!current || current.state !== "running" || waiting) return;
    const at = ticks[step];
    if (step === failAt) {
      stop(job.id);
      const error = problemOf("disk_full", {
        log: "[libvpx-vp9] av_interleaved_write_frame(): No space left on device",
      });
      put(db, {
        ...current,
        state: "failed",
        error,
        items: current.items.map((it, i) =>
          i === at?.item ? { ...it, state: "failed", error } : it,
        ),
      });
      return;
    }
    if (!at) {
      stop(job.id);
      put(db, finishedRender(db, project, current, plans, blobs, revision));
      return;
    }
    // The item's last tick waits for its file to be drawn.
    const plan = plans[at.item] as ItemPlan;
    const lastOfItem = ticks[step + 1]?.item !== at.item;
    if (lastOfItem && plan.file && !blobs.has(at.item)) {
      waiting = true;
      void plan.file.then((blob) => {
        blobs.set(at.item, blob);
        waiting = false;
      });
      return;
    }
    step += 1;
    put(db, {
      ...current,
      progress: {
        fraction: step / ticks.length,
        stage: at.stage,
        attempt:
          at.attempt && at.attemptN && at.attemptN > 1
            ? { n: at.attemptN, concessions: at.attempt.concessions }
            : undefined,
      },
      items: current.items.map((it, i): JobItem => {
        if (i < at.item) return { ...it, state: "done", fraction: 1 };
        if (i > at.item) return it;
        const state = at.stage === "validate" ? "validating" : "exporting";
        return { ...it, state, fraction: at.fraction };
      }),
    });
  };
  timers.set(job.id, setInterval(tick, TICK_MS));
}

/* ------------------------------------------------------------------- Publish */

const publishing = (db: MockDb) =>
  [...db.jobs.values()].some((j) => j.kind === "publish" && j.state === "running");

function publishStage(fraction: number): Job["progress"]["stage"] {
  return fraction < 0.6 ? "pass2" : "send";
}

type PublishRequest = Extract<JobRequest, { kind: "publish" }>;

/** The publication done: its stickers in the pack (a new one is made), «Недавние» updated, as the
 *  server does it (packChanged after the stickers, settingsChanged at the end). */
function published(db: MockDb, job: Job, request: PublishRequest): Job {
  const name = job.pack ?? "";
  const project = db.projects.find((p) => p.id === request.projectId);
  const items = job.items.map((item, i): JobItem => ({
    ...item,
    state: "done",
    fraction: 1,
    sticker: { pack: name, stickerId: `sticker-${job.id}-${i}` },
  }));
  let pack = db.packs.find((p) => p.name === name);
  if (!pack && request.target.kind === "new") {
    pack = {
      name,
      title: request.target.title,
      type: request.target.type,
      count: 0,
      limit: request.target.type === "custom_emoji" ? 200 : 120,
      status: "ok",
      checkedAt: new Date().toISOString(),
      link: `https://t.me/addstickers/${name}`,
      stickers: [],
    };
    db.packs.push(pack);
  }
  if (pack) {
    for (const [i, item] of request.items.entries()) {
      pack.stickers.push({
        id: `sticker-${job.id}-${i}`,
        emoji: item.emoji,
        mediaUrl: `/api/v1/packs/${name}/stickers/sticker-${job.id}-${i}/media`,
        format: "video",
        origin: project && {
          projectId: project.id,
          projectName: project.name,
          segmentId: item.segmentId,
          projectStatus: "ok",
        },
      });
    }
    pack.count = pack.stickers.length;
    fakeHub.emit("packChanged", packSummary(pack));
  }
  const sent = request.items.flatMap((item) => item.emoji);
  db.settings = {
    ...db.settings,
    recentEmoji: [...new Set([...sent, ...db.settings.recentEmoji])].slice(0, 24),
  };
  fakeHub.emit("settingsChanged", db.settings);
  return { ...job, state: "succeeded", progress: { ...job.progress, fraction: 1 }, items };
}

function startPublish(db: MockDb, job: Job, request: PublishRequest) {
  let step = 0;
  timers.set(
    job.id,
    setInterval(() => {
      const current = db.jobs.get(job.id);
      if (!current) return;
      if (current.state === "queued") {
        // The second publish waits until the first one is over (artboard 12).
        if (!publishing(db)) put(db, { ...current, state: "running" });
        return;
      }
      step += 1;
      if (step >= STEPS) {
        stop(job.id);
        put(db, published(db, current, request));
        return;
      }
      const fraction = step / STEPS;
      put(db, {
        ...current,
        progress: { ...current.progress, fraction, stage: publishStage(fraction) },
        items: current.items.map((it) => ({ ...it, state: "sending", fraction })),
      });
    }, TICK_MS),
  );
}

/* ------------------------------------------------------------------- All jobs */

export function startJob(db: MockDb, request: JobRequest): Job {
  // A new preview of the same project cancels the previous one (3c D).
  if (request.kind === "preview") {
    for (const job of db.jobs.values()) {
      if (
        job.kind === "preview" &&
        job.projectId === request.projectId &&
        job.state === "running"
      ) {
        cancelJob(db, job.id);
      }
    }
  }
  const busy = publishing(db);
  const job: Job = {
    id: crypto.randomUUID(),
    kind: request.kind,
    projectId: request.projectId,
    state: request.kind === "publish" && busy ? "queued" : "running",
    createdAt: new Date().toISOString(),
    progress: { fraction: 0, stage: "probe" },
    items: itemsOf(request),
    pack:
      request.kind !== "publish"
        ? undefined
        : request.target.kind === "existing"
          ? request.target.pack
          : `${request.target.name}_by_${app.botUsername}`,
  };
  put(db, job);
  if (request.kind === "publish") startPublish(db, job, request);
  else startRender(db, job, request);
  return job;
}

export function cancelJob(db: MockDb, id: string): Job | undefined {
  const job = db.jobs.get(id);
  if (!job) return undefined;
  stop(id);
  const canceled: Job = { ...job, state: "canceled" };
  put(db, canceled);
  return canceled;
}
