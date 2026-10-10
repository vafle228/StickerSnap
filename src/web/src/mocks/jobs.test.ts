import { afterEach, describe, expect, it, vi } from "vitest";
import type { Job, JobRequest } from "../shared/api/types";
import { createDb } from "./data";
import { fakeHub } from "./fakeHub";
import { startJob } from "./jobs";

const PROJECT = "6f1d2c3a-1b2c-4d5e-8f90-0a1b2c3d4e51";

const publish: JobRequest = {
  kind: "publish",
  projectId: PROJECT,
  revision: 3,
  items: [{ segmentId: "seg-1", emoji: ["😺"] }],
  target: { kind: "existing", pack: "kot_divan_by_StickerSnapAppBot" },
};

describe("demo jobs", () => {
  afterEach(() => vi.useRealTimers());

  it("plays progress as jobUpdated and ends with the result", () => {
    vi.useFakeTimers();
    const db = createDb("ok");
    const seen: Job[] = [];
    const off = fakeHub.on("jobUpdated", (job) => seen.push(job));

    const job = startJob(db, {
      kind: "export",
      projectId: PROJECT,
      revision: 3,
      segmentIds: ["seg-1"],
    });
    vi.advanceTimersByTime(5000);
    off();

    expect(seen.length).toBeGreaterThan(5);
    expect(db.jobs.get(job.id)?.state).toBe("succeeded");
  });

  it("queues the second publish until the first one is over (artboard 12)", () => {
    vi.useFakeTimers();
    const db = createDb("ok");
    const first = startJob(db, publish);
    const second = startJob(db, publish);
    expect(second.state).toBe("queued");

    vi.advanceTimersByTime(3500);
    expect(db.jobs.get(first.id)?.state).toBe("succeeded");
    expect(db.jobs.get(second.id)?.state).toBe("running");
  });
});
