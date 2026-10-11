import { HttpResponse } from "msw";
import type { Project, Segment, SourceRef, ValidationReport } from "../shared/api/types";
import { app, summaryOf, type MockDb } from "./data";
import { bytesPerFrame, estimateOf } from "./encoder";
import { fakeHub } from "./fakeHub";
import { problem } from "./route";
import { extOf, isFormat, probe, projectName, sourceOf, validationOf } from "./sources";

/* Import, the size forecast, the .webm check and rendered files of the demo server. */

/** Reading a video takes a moment, so «Читаю файл» (3c, A) shows with its «Отменить». */
const READ_MS = 1500;

const wait = (ms: number, signal: AbortSignal) =>
  new Promise<void>((resolve) => {
    const timer = setTimeout(resolve, ms);
    signal.addEventListener("abort", () => {
      clearTimeout(timer);
      resolve();
    });
  });

/** The file behind a SourceRef: an upload has its bytes, a desktop path only its name. */
function fileOf(db: MockDb, source: SourceRef): { name: string; file?: File } | undefined {
  if (source.kind === "path") return { name: source.path.split(/[\\/]/).pop() ?? source.path };
  const file = db.uploads.get(source.uploadId);
  return file && { name: file.name, file };
}

/** POST /api/v1/uploads: the browser's file is kept for the requests that refer to it. */
export async function upload(db: MockDb, request: Request) {
  const form = await request.formData().catch(() => undefined);
  const file = form?.get("file");
  if (!(file instanceof File)) return problem("bad_request");
  const uploadId = crypto.randomUUID();
  db.uploads.set(uploadId, file);
  return { status: 201 as const, body: { uploadId } };
}

/** POST /api/v1/projects: a new project with one segment, named after its video. The format is
 *  told by the file (here: its extension); «Битый файл» finds no whole frame in any video. */
export async function createProject(db: MockDb, source: SourceRef, request: Request) {
  await wait(READ_MS, request.signal);
  // «Отменить»: the request was dropped, nothing is created
  if (request.signal.aborted) return problem("bad_request");
  const found = fileOf(db, source);
  if (!found) return problem("source_not_found");
  const ext = extOf(found.name);
  if (!isFormat(ext)) return problem("format_unsupported", { params: { ext } });
  if (db.scenario === "import-broken") {
    return problem("source_unreadable", {
      log: `[mov,mp4,m4a,3gp,3g2,mj2 @ 0x55d0c8a1e2c0] moov atom not found\n${found.name}: Invalid data found when processing input`,
    });
  }

  const template = structuredClone(db.template);
  const id = crypto.randomUUID();
  const info = sourceOf(found.name, found.file?.size ?? template.source.sizeBytes, template.source);
  const whole = { x: 0, y: 0, width: info.width, height: info.height };
  const side = Math.min(info.width, info.height);
  const fps = info.fps.num / info.fps.den;
  const first: Segment = {
    ...(template.scene.segments[0] as Segment),
    id: "seg-1",
    name: "Стикер 1",
    inFrame: 0,
    // The first 3 s, or the whole clip when it is shorter
    outFrame: Math.min(info.frameCount, Math.round(fps * 3)) - 1,
    frame: {
      mode: "crop",
      preset: info.width === info.height ? "source" : "square",
      rect:
        info.width === info.height
          ? whole
          : { x: (info.width - side) / 2, y: (info.height - side) / 2, width: side, height: side },
    },
  };
  const project: Project = {
    ...template,
    id,
    name: projectName(found.name),
    path: `${db.settings.folders.projects}/${projectName(found.name)}.ssproj`,
    revision: 1,
    savedAt: new Date().toISOString(),
    source: info,
    mediaUrl: `/api/v1/projects/${id}/media`,
    scene: { ...template.scene, segments: [first] },
    exports: {},
  };
  db.projects.unshift(project);
  fakeHub.emit("projectChanged", summaryOf(project));
  return { status: 201 as const, body: project };
}

/** «Чужой .webm»: a file made elsewhere, too heavy, 60 fps and not 512 on any side. */
function foreignReport(fileName: string): ValidationReport {
  return validationOf(
    {
      fileName,
      bytes: 277_504,
      container: "webm",
      codec: "vp9",
      width: 640,
      height: 360,
      durationSec: 2.5,
      fps: 60,
      audio: "opus",
      alpha: "none",
    },
    app.limits.sticker,
  );
}

/** POST /api/v1/validate: the dropped file's size is its own, and so are its sides and length
 *  as far as the browser can read them; the codec is taken from the container. */
export async function validate(db: MockDb, source: SourceRef) {
  const found = fileOf(db, source);
  if (!found) return problem("source_not_found");
  const ext = extOf(found.name);
  if (ext !== "webm") return problem("format_unsupported", { params: { ext } });
  if (db.scenario === "webm-bad") return { status: 200 as const, body: foreignReport(found.name) };
  const probed = found.file ? await probe(found.file) : undefined;
  if (found.file && !probed) {
    return problem("source_unreadable", { log: `${found.name}: EBML header parsing failed` });
  }
  const report = validationOf(
    {
      fileName: found.name,
      bytes: found.file?.size ?? 0,
      container: "webm",
      codec: "vp9",
      width: probed?.width ?? 512,
      height: probed?.height ?? 512,
      durationSec: probed?.durationSec ?? 3,
      fps: 30,
      audio: "none",
      alpha: "none",
    },
    app.limits.sticker,
  );
  return { status: 200 as const, body: report };
}

/** POST /api/v1/projects/{id}/estimate: the encoder model's first rung (encoder.ts). */
export function estimate(db: MockDb, id: string, segment: Segment) {
  const project = db.projects.find((p) => p.id === id);
  if (!project) return problem("project_not_found");
  const limits = app.limits[segment.preset];
  const body = estimateOf(project.source, segment, limits, bytesPerFrame(db.scenario));
  return { status: 200 as const, body };
}

/** GET /api/v1/files/{fileId}: a rendered .webm. */
export function file(db: MockDb, fileId: string) {
  const blob = db.files.get(fileId);
  if (!blob) return problem("source_not_found");
  return new HttpResponse(blob, { headers: { "Content-Type": "video/webm" } });
}
