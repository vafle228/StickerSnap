import type { Job, JobItem, JobRequest } from "../shared/api/types";
import type { MockDb } from "./data";
import { fakeHub } from "./fakeHub";

/* Background jobs played by timer: progress through the stages, then the result, each step
   sent as jobUpdated with the whole Job (contract, «Фоновые задачи и события»). */

const TICK_MS = 300;
const STEPS = 10;
const timers = new Map<string, ReturnType<typeof setInterval>>();

function put(db: MockDb, job: Job) {
  db.jobs.set(job.id, job);
  fakeHub.emit("jobUpdated", job);
}

function stageAt(kind: Job["kind"], fraction: number): Job["progress"]["stage"] {
  if (kind === "publish") return fraction < 0.6 ? "pass2" : "send";
  if (fraction < 0.1) return "probe";
  if (fraction < 0.5) return "pass1";
  return fraction < 0.9 ? "pass2" : "validate";
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

function finished(job: Job): Job {
  const items = job.items.map((item, i): JobItem => {
    const bytes = 180_000 + i * 21_000;
    if (job.kind === "preview") {
      return {
        ...item,
        state: "done",
        fraction: 1,
        preview: { fileUrl: `/api/v1/files/preview-${job.id}`, bytes },
      };
    }
    if (job.kind === "publish") {
      return {
        ...item,
        state: "done",
        fraction: 1,
        sticker: { pack: job.pack ?? "", stickerId: `sticker-${job.id}-${i}` },
      };
    }
    return { ...item, state: "done", fraction: 1 };
  });
  return {
    ...job,
    state: "succeeded",
    progress: { ...job.progress, fraction: 1, stage: "validate" },
    items,
  };
}

const publishing = (db: MockDb) =>
  [...db.jobs.values()].some((j) => j.kind === "publish" && j.state === "running");

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
      request.kind === "publish" && request.target.kind === "existing"
        ? request.target.pack
        : undefined,
  };
  put(db, job);
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
        clearInterval(timers.get(job.id));
        timers.delete(job.id);
        put(db, finished(current));
        return;
      }
      const fraction = step / STEPS;
      put(db, {
        ...current,
        progress: { ...current.progress, fraction, stage: stageAt(current.kind, fraction) },
        items: current.items.map((it) => ({
          ...it,
          state: current.kind === "publish" ? "sending" : "exporting",
          fraction,
        })),
      });
    }, TICK_MS),
  );
  return job;
}

export function cancelJob(db: MockDb, id: string): Job | undefined {
  const job = db.jobs.get(id);
  if (!job) return undefined;
  clearInterval(timers.get(id));
  timers.delete(id);
  const canceled: Job = { ...job, state: "canceled" };
  put(db, canceled);
  return canceled;
}
