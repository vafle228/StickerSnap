import type {
  AccountLink,
  AppInfo,
  Job,
  Pack,
  PackSummary,
  Project,
  ProjectSummary,
  Segment,
  Settings,
} from "../shared/api/types";
import type { Scenario } from "./scenarios";

/* Demo data, strictly of the contract's types. Everything a screen needs that is missing here
   is a change to the contract document first, not a field invented in this file. */

const BOT = "StickerSnapAppBot";
const ago = (minutes: number) => new Date(Date.now() - minutes * 60_000).toISOString();

export const app: AppInfo = {
  appVersion: "0.1.0",
  ffmpegVersion: "7.1",
  apiVersion: "1.0.0",
  mode: "web",
  botUsername: BOT,
  limits: {
    sticker: { maxBytes: 262144, side: 512, maxDurationSec: 3, maxFps: 30 },
    emoji: { maxBytes: 65536, side: 100, maxDurationSec: 3, maxFps: 30 },
    packCapacity: { regular: 120, custom_emoji: 200 },
  },
};

function segment(
  id: string,
  name: string,
  inFrame: number,
  outFrame: number,
  preset: Segment["preset"] = "sticker",
): Segment {
  return {
    id,
    name,
    inFrame,
    outFrame,
    excludedFrames: [],
    frame: { mode: "crop", preset: "square", rect: { x: 140, y: 0, width: 360, height: 360 } },
    speed: 1,
    loop: "normal",
    preset,
    longSticker: false,
    autoFit: true,
  };
}

function project(id: string, name: string, minutesAgo: number, segments: Segment[]): Project {
  return {
    id,
    name,
    path: `C:/Users/demo/StickerSnap/${name}.ssproj`,
    revision: 3,
    savedAt: ago(minutesAgo),
    source: {
      fileName: `${name}.mp4`,
      path: `C:/Users/demo/Videos/${name}.mp4`,
      status: "ok",
      format: "mp4",
      codec: "h264",
      width: 640,
      height: 360,
      fps: { num: 30, den: 1 },
      frameCount: 240,
      durationSec: 8,
      hasAlpha: false,
      sizeBytes: 1_830_000,
    },
    mediaUrl: `/api/v1/projects/${id}/media`,
    scene: { schemaVersion: 1, layers: [{ id: "layer-1", kind: "video" }], segments },
    exports: {},
  };
}

export function summaryOf(p: Project): ProjectSummary {
  const presets = new Set(p.scene.segments.map((s) => s.preset));
  const fps = p.source.fps.num / p.source.fps.den;
  return {
    id: p.id,
    name: p.name,
    path: p.path,
    updatedAt: p.savedAt,
    segmentCount: p.scene.segments.length,
    preset: presets.size > 1 ? "mixed" : (p.scene.segments[0]?.preset ?? "sticker"),
    durationSec: Math.max(
      0,
      ...p.scene.segments.map((s) => (s.outFrame - s.inFrame + 1) / fps / s.speed),
    ),
    coverUrl: `/api/v1/projects/${p.id}/cover`,
    sourceStatus: p.source.status,
  };
}

/* Timeline scenarios change the first project, the one the editor reopens after F5. */

/** Two minutes of Full HD at 29,97 fps: the scale and the scroll, overlapping segments. */
function longVideo(p: Project) {
  p.source = {
    ...p.source,
    width: 1920,
    height: 1080,
    fps: { num: 30000, den: 1001 },
    frameCount: 3596,
    durationSec: 119.99,
    sizeBytes: 48_200_000,
  };
  const square = { x: 420, y: 0, width: 1080, height: 1080 };
  p.scene.segments = [
    segment("seg-1", "Стикер 1", 300, 388),
    segment("seg-2", "Стикер 2", 1200, 1259),
    segment("seg-3", "Стикер 3", 1230, 1318),
    segment("seg-4", "Стикер 4", 3507, 3595),
  ].map((s) => ({ ...s, frame: { mode: "crop", preset: "square", rect: square } }));
}

/** A 1,5 s GIF at 12 fps: few enough frames that «Обзор» already shows each one. */
function shortGif(p: Project) {
  p.source = {
    ...p.source,
    fileName: "Кот и клавиатура.gif",
    format: "gif",
    codec: "gif",
    width: 480,
    height: 480,
    fps: { num: 12, den: 1 },
    frameCount: 18,
    durationSec: 1.5,
    hasAlpha: true,
    sizeBytes: 640_000,
  };
  const whole = { x: 0, y: 0, width: 480, height: 480 };
  p.scene.segments = [segment("seg-1", "Стикер 1", 0, 17)].map((s) => ({
    ...s,
    frame: { mode: "crop", preset: "square", rect: whole },
  }));
}

function pack(name: string, title: string, count: number, minutesAgo: number): PackSummary {
  return {
    name: `${name}_by_${BOT}`,
    title,
    type: "regular",
    count,
    limit: 120,
    status: "ok",
    checkedAt: ago(minutesAgo),
  };
}

/** The server's state for one scenario; handlers read and change it. */
export interface MockDb {
  scenario: Scenario;
  settings: Settings;
  /** «Недавние»; the server answers them newest first */
  projects: Project[];
  /** Taken off the list with «Убрать из списка»: the file is still there to open by path */
  forgotten: Project[];
  /** What a new project is made of: demo data has no video to read */
  template: Project;
  jobs: Map<string, Job>;
  /** Files the browser uploaded, by uploadId */
  uploads: Map<string, File>;
  /** Rendered .webm files of previews and exports, by fileId */
  files: Map<string, Blob>;
  account: AccountLink;
  packs: Pack[];
}

export function createDb(scenario: Scenario): MockDb {
  const projects = [
    project("6f1d2c3a-1b2c-4d5e-8f90-0a1b2c3d4e51", "Кот и клавиатура", 12, [
      segment("seg-1", "Стикер 1", 0, 89),
      segment("seg-2", "Стикер 2", 120, 179),
    ]),
    project("6f1d2c3a-1b2c-4d5e-8f90-0a1b2c3d4e52", "Реакции с созвона", 180, [
      segment("seg-1", "Стикер 1", 30, 119),
    ]),
    project("6f1d2c3a-1b2c-4d5e-8f90-0a1b2c3d4e53", "Пёс на диване", 60 * 26, [
      segment("seg-1", "Стикер 1", 0, 59),
    ]),
    project(
      "6f1d2c3a-1b2c-4d5e-8f90-0a1b2c3d4e54",
      "Эмодзи для чата",
      60 * 30,
      [1, 2, 3, 4, 5].map((n) => segment(`seg-${n}`, `Эмодзи ${n}`, n * 30, n * 30 + 44, "emoji")),
    ),
    project("6f1d2c3a-1b2c-4d5e-8f90-0a1b2c3d4e55", "Танец под припев", 60 * 24 * 8, [
      segment("seg-1", "Стикер 1", 10, 84),
    ]),
    project(
      "6f1d2c3a-1b2c-4d5e-8f90-0a1b2c3d4e56",
      "Лето на даче",
      60 * 24 * 13,
      [1, 2, 3, 4].map((n) => segment(`seg-${n}`, `Стикер ${n}`, n * 40, n * 40 + 35)),
    ),
    project("6f1d2c3a-1b2c-4d5e-8f90-0a1b2c3d4e57", "Первый тест", 60 * 24 * 27, [
      segment("seg-1", "Стикер 1", 0, 35),
    ]),
  ];
  const template = structuredClone(projects[0] as Project);
  if (scenario === "source-missing" && projects[0]) {
    projects[0].source = { ...projects[0].source, status: "missing" };
  }
  if (scenario === "long-video" && projects[0]) longVideo(projects[0]);
  if (scenario === "short-gif" && projects[0]) shortGif(projects[0]);
  // Every segment deleted: the editor still opens, export waits for a new one.
  if (scenario === "no-segments" && projects[0]) projects[0].scene.segments = [];
  // The first launch: nothing made yet, «Проекты» shows its empty state.
  if (scenario === "no-projects") projects.length = 0;

  const packs: Pack[] = [
    pack("reactions_call", "Реакции с созвона", 14, 2),
    pack("kot_divan", "Кот на диване", scenario === "pack-full" ? 120 : 37, 60),
  ].map((p) => ({ ...p, link: `https://t.me/addstickers/${p.name}`, stickers: [] }));

  const user = { id: 100200300, username: "demo", firstName: "Дима" };
  const account: AccountLink =
    scenario === "offline"
      ? { state: "error", code: "network_unavailable", user }
      : { state: "linked", user };

  return {
    scenario,
    settings: {
      theme: "system",
      accent: "indigo",
      folders: {
        projects: "C:/Users/demo/StickerSnap",
        stickers: "C:/Users/demo/StickerSnap/Стикеры",
      },
      recentEmoji: ["😂", "🔥", "👍", "😺"],
    },
    projects,
    forgotten: [],
    template,
    jobs: new Map(),
    uploads: new Map(),
    files: new Map(),
    account,
    packs,
  };
}

export function packSummary(pack: Pack): PackSummary {
  const summary: PackSummary & Partial<Pick<Pack, "link" | "stickers">> = { ...pack };
  delete summary.link;
  delete summary.stickers;
  return summary;
}
