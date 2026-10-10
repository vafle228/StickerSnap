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

function segment(id: string, name: string, inFrame: number, outFrame: number): Segment {
  return {
    id,
    name,
    inFrame,
    outFrame,
    excludedFrames: [],
    frame: { mode: "crop", preset: "square", rect: { x: 140, y: 0, width: 360, height: 360 } },
    speed: 1,
    loop: "normal",
    preset: "sticker",
    longSticker: false,
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
  projects: Project[];
  jobs: Map<string, Job>;
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
  ];
  if (scenario === "source-missing" && projects[0]) {
    projects[0].source = { ...projects[0].source, status: "missing" };
  }

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
    jobs: new Map(),
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
