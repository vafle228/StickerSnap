import type {
  AccountLink,
  AppInfo,
  Download,
  Job,
  Pack,
  PackSticker,
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
  ffmpeg: { status: "ok", version: "9.0.2" },
  apiVersion: "1.0.0",
  mode: "web",
  botUsername: BOT,
  limits: {
    sticker: { maxBytes: 262144, side: 512, maxDurationSec: 3, longMaxDurationSec: 10, maxFps: 30 },
    emoji: { maxBytes: 262144, side: 100, maxDurationSec: 3, longMaxDurationSec: 10, maxFps: 30 },
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

function pack(
  name: string,
  title: string,
  count: number,
  minutesAgo: number,
  type: PackSummary["type"] = "regular",
): PackSummary {
  return {
    name: `${name}_by_${BOT}`,
    title,
    type,
    count,
    limit: type === "custom_emoji" ? 200 : 120,
    status: "ok",
    checkedAt: ago(minutesAgo),
  };
}

const REACTIONS = "6f1d2c3a-1b2c-4d5e-8f90-0a1b2c3d4e52";
const CAT = "6f1d2c3a-1b2c-4d5e-8f90-0a1b2c3d4e51";

/** Stickers of a pack: emoji and the project each came from. Telegram serves the files, demo
 *  data has none, so the tiles draw placeholders. */
function stickers(packName: string, list: [emoji: string[], origin?: PackSticker["origin"]][]) {
  return list.map(([emoji, origin], i): PackSticker => {
    const id = `${packName}-${i + 1}`;
    return {
      id,
      emoji,
      mediaUrl: `/api/v1/packs/${packName}_by_${BOT}/stickers/${id}/media`,
      format: "video",
      origin,
    };
  });
}

/** «Ошибки отправки»: the publish job that Telegram asked to wait */
export const PUBLISH_JOB = "publish-summer";

const from = (projectId: string, projectName: string, segmentId: string) => ({
  projectId,
  projectName,
  segmentId,
  projectStatus: "ok" as const,
});

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
  /** Downloads by link (artboard 20), newest first */
  downloads: Download[];
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

  // Artboard 7: a sticker pack open with twelve stickers, one being sent, one of emoji.
  const reactions = from(REACTIONS, "Реакции с созвона", "seg-1");
  const packs: Pack[] = [
    {
      ...pack("reactions_call", "Реакции с созвона", 12, 2),
      stickers: stickers("reactions_call", [
        [["😂", "🤣"], reactions],
        [["👍"], reactions],
        [["😴", "💤"], reactions],
        [["🙈"], reactions],
        [["🔥", "❤️", "🎉"], reactions],
        [["🤔"], from(CAT, "Кот и клавиатура", "seg-2")],
        [["😮"], reactions],
        [["👋"], reactions],
        // Its project was deleted: the sticker stays, the link to it is gone
        [
          ["🙏"],
          {
            ...from("6f1d2c3a-1b2c-4d5e-8f90-0a1b2c3d4e00", "Старый созвон", "seg-1"),
            projectStatus: "missing",
          },
        ],
        [["😎"], reactions],
        // Published from another app: no project behind it
        [["🥲"]],
        [["✅"], reactions],
      ]),
    },
    {
      ...pack("kot_divan", "Кот и диван", scenario === "pack-full" ? 120 : 5, 60),
      stickers: stickers("kot_divan", [
        [["😹", "🛋"], from(CAT, "Кот и клавиатура", "seg-1")],
        [["🙀"], from(CAT, "Кот и клавиатура", "seg-2")],
        [["😺"], from(CAT, "Кот и клавиатура", "seg-1")],
        [["🐈"], from(CAT, "Кот и клавиатура", "seg-2")],
        [["💤"], from(CAT, "Кот и клавиатура", "seg-1")],
      ]),
    },
    {
      ...pack("chat_emoji", "Эмодзи для чата", 6, 60 * 26, "custom_emoji"),
      stickers: stickers(
        "chat_emoji",
        ["✨", "💯", "👀", "🫶", "🙌", "😅"].map((e, i) => [
          [e],
          from("6f1d2c3a-1b2c-4d5e-8f90-0a1b2c3d4e54", "Эмодзи для чата", `seg-${(i % 5) + 1}`),
        ]),
      ),
    },
    {
      ...pack("summer_2026", "Лето 2026", 2, 5),
      // «Ошибки отправки»: two stickers are in, two wait for Telegram (artboard 7-6)
      sending:
        scenario === "publish-errors" ? { jobId: PUBLISH_JOB, done: 2, total: 4 } : undefined,
      stickers: stickers("summer_2026", [
        [["☀️"], from("6f1d2c3a-1b2c-4d5e-8f90-0a1b2c3d4e56", "Лето на даче", "seg-1")],
        [["🍉"], from("6f1d2c3a-1b2c-4d5e-8f90-0a1b2c3d4e56", "Лето на даче", "seg-2")],
      ]),
    },
    {
      ...pack("first_test", "Первый тест", 1, 60 * 24 * 20),
      // «Пак пропал»: someone deleted it through @Stickers
      status: scenario === "pack-missing" ? ("missing" as const) : ("ok" as const),
      stickers: stickers("first_test", [
        [["🐶"], from("6f1d2c3a-1b2c-4d5e-8f90-0a1b2c3d4e57", "Первый тест", "seg-1")],
      ]),
    },
  ].map((p) => ({ ...p, link: `https://t.me/addstickers/${p.name}` }));
  if (scenario === "no-packs") packs.length = 0;
  // «Пак заполнен»: 120 of 120, every one of them in the grid
  const full = packs[1];
  if (scenario === "pack-full" && full) {
    const first = full.stickers.slice();
    full.stickers = Array.from({ length: 120 }, (_, i) => ({
      ...(first[i % first.length] as PackSticker),
      id: `kot_divan-${i + 1}`,
    }));
  }

  const user = { id: 100200300, username: "demo", firstName: "Дима" };
  const device = "DESKTOP-7F3K2Q";
  const account: AccountLink =
    scenario === "offline"
      ? { state: "error", code: "network_unavailable", user, device }
      : scenario === "unlinked" || scenario === "link-declined"
        ? { state: "unlinked" }
        : scenario === "link-wait"
          ? demoLink()
          : { state: "linked", user, device };

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
    jobs: new Map(scenario === "publish-errors" ? [[PUBLISH_JOB, waitingPublish()]] : []),
    uploads: new Map(),
    files: new Map(),
    downloads: [],
    account,
    packs,
  };
}

/** Artboard 7-6: two of four stickers are in «Лето 2026», Telegram asked to wait with the rest. */
function waitingPublish(): Job {
  const sticker = (n: number) => ({ pack: `summer_2026_by_${BOT}`, stickerId: `summer_2026-${n}` });
  return {
    id: PUBLISH_JOB,
    kind: "publish",
    projectId: "6f1d2c3a-1b2c-4d5e-8f90-0a1b2c3d4e56",
    state: "running",
    createdAt: ago(1),
    progress: {
      fraction: 0.5,
      stage: "wait",
      retryAt: new Date(Date.now() + 24_000).toISOString(),
    },
    items: [
      { segmentId: "seg-1", state: "done", fraction: 1, sticker: sticker(1) },
      { segmentId: "seg-2", state: "done", fraction: 1, sticker: sticker(2) },
      { segmentId: "seg-3", state: "queued", fraction: 0 },
      { segmentId: "seg-4", state: "queued", fraction: 0 },
    ],
    pack: `summer_2026_by_${BOT}`,
  };
}

export function packSummary(pack: Pack): PackSummary {
  const summary: PackSummary & Partial<Pick<Pack, "link" | "stickers">> = { ...pack };
  delete summary.link;
  delete summary.stickers;
  return summary;
}

/** A link waiting for the answer in Telegram, as the server gives it out: ten minutes to live. */
export function demoLink(): Extract<AccountLink, { state: "pending" }> {
  return {
    state: "pending",
    deepLink: `https://t.me/StickerSnapAppBot?startapp=link_demo${Date.now()}`,
    expiresAt: new Date(Date.now() + 10 * 60_000).toISOString(),
  };
}
