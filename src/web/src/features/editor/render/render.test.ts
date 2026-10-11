import { describe, expect, it } from "vitest";
import type { ExportResult, Job, Segment } from "../../../shared/api/types";
import { ladderText, lostOf, sideUntouched, stageLine, stageTitle, waiting } from "./format";
import { exportToast } from "./toast";

const segment = (id: string, name: string): Segment => ({
  id,
  name,
  inFrame: 0,
  outFrame: 89,
  excludedFrames: [],
  frame: { mode: "crop", preset: "square", rect: { x: 0, y: 0, width: 360, height: 360 } },
  speed: 1,
  loop: "normal",
  preset: "sticker",
  longSticker: false,
});

const segments = [segment("seg-1", "Стикер 1"), segment("seg-2", "Стикер 2")];

function job(patch: Partial<Job>): Job {
  return {
    id: "job-1",
    kind: "export",
    projectId: "p",
    state: "running",
    createdAt: "2026-10-11T00:00:00Z",
    progress: { fraction: 0.41, stage: "pass2" },
    items: [{ segmentId: "seg-1", state: "exporting", fraction: 0.64 }],
    ...patch,
  };
}

const result: ExportResult = {
  segmentId: "seg-1",
  sceneRevision: 4,
  stale: false,
  fileUrl: "/api/v1/files/f",
  bytes: 257_024,
  output: { width: 512, height: 512, fps: 20, durationSec: 2.84 },
  crf: 37,
  concessions: [
    { kind: "fps", from: 30, to: 20 },
    { kind: "crf", from: 31, to: 37 },
  ],
  ladder: [
    { label: [], bytes: 337_920 },
    { label: [{ kind: "fps", from: 30, to: 24 }], bytes: 297_984 },
    { label: [{ kind: "fps", from: 30, to: 20 }], bytes: 274_432 },
    {
      label: [
        { kind: "fps", from: 30, to: 20 },
        { kind: "crf", from: 31, to: 37 },
      ],
      bytes: 257_024,
    },
  ],
  validation: { fileName: "Стикер 1.webm", bytes: 257_024, verdict: "pass", checks: [] },
};

describe("export progress words (3c, E)", () => {
  it("says the pass and the attempt with what it gave up", () => {
    const running = job({
      progress: {
        fraction: 0.64,
        stage: "pass2",
        attempt: { n: 2, concessions: [{ kind: "fps", from: 30, to: 24 }] },
      },
    });
    expect(stageTitle(running, running.items[0])).toBe("Кодирую");
    expect(stageLine(running, running.items[0])).toBe("Проход 2 из 2 · попытка 2: 24 fps");
  });

  it("lets a later segment wait, but not the first one before its first tick", () => {
    const all = job({
      items: [
        { segmentId: "seg-1", state: "queued", fraction: 0 },
        { segmentId: "seg-2", state: "queued", fraction: 0 },
      ],
    });
    expect(waiting(all, all.items[0])).toBe(false);
    expect(waiting(all, all.items[1])).toBe(true);
    expect(stageTitle(all, all.items[1])).toBe("Ждёт очереди");
  });
});

describe("the export popup (3c, F)", () => {
  it("counts the segments of «Экспортировать все отрезки»", () => {
    const toast = exportToast(
      job({
        progress: { fraction: 0.7, stage: "pass1" },
        items: [
          { segmentId: "seg-1", state: "done", fraction: 1 },
          { segmentId: "seg-2", state: "exporting", fraction: 0.4 },
        ],
      }),
      segments,
    );
    expect(toast?.title).toBe("Экспорт 2 из 2");
    expect(toast?.description).toBe("Стикер 2 · кодирую");
    expect(toast?.progress).toBe(70);
  });

  it("turns into the result with «с уступками»", () => {
    const toast = exportToast(
      job({
        state: "succeeded",
        items: [{ segmentId: "seg-1", state: "done", fraction: 1, export: result }],
      }),
      segments,
    );
    expect(toast?.tone).toBe("success");
    expect(toast?.title).toBe("Стикер 1.webm готов");
    expect(toast?.badge?.label).toBe("с уступками");
    expect(toast?.description).toBe("251 КБ · проверка Telegram пройдена");
  });

  it("says what broke when the export failed", () => {
    const toast = exportToast(
      job({
        state: "failed",
        error: {
          type: "about:blank",
          status: 507,
          title: "disk full",
          code: "disk_full",
          traceId: "t-1",
          log: "No space left on device",
        },
      }),
      segments,
    );
    expect(toast?.tone).toBe("danger");
    expect(toast?.title).toBe("Экспорт не удался");
    expect(toast?.description).toBe("Не хватает места на диске");
    expect(toast?.details?.log).toBe("No space left on device");
  });
});

describe("what was given up (3c, H)", () => {
  it("shows «было → стало» where quality was lowered", () => {
    expect(lostOf(result).map((l) => [l.value, l.given])).toEqual([
      ["30 → 20 fps", true],
      ["CRF 31 → 37", true],
      ["512 · без изменений", false],
    ]);
  });

  it("names every rung of the ladder from its first fps", () => {
    expect(result.ladder.map((step) => ladderText(step, result))).toEqual([
      "30 fps · 330 КБ",
      "24 fps · 291 КБ",
      "20 fps · 268 КБ",
      "20 fps + CRF 37 · 251 КБ",
    ]);
    expect(sideUntouched(result)).toBe(true);
  });
});
