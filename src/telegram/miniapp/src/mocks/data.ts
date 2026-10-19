import type {
  Device,
  FeedbackRequest,
  LinkRequest,
  Pack,
  PackDetail,
  PackKind,
  Sticker,
} from "../api/types";
import type { Scenario } from "./scenarios";

/* The demo data of the Mini App: the packs and computers of artboard 13, as the bot service
   would send them (src/contract/bot-service.yaml). */

export const BOT = "StickerSnapAppBot";

const LIMIT: Record<PackKind, number> = { stickers: 120, emoji: 200 };

/** The artboards draw stickers as soft shapes in the prism colours */
const COLORS = ["#b9a6ff", "#ffb38a", "#8fb8ff", "#f7a8d8"] as const;
const SHAPES = ["circle", "square", "circle", "rounded"] as const;
export type Shape = (typeof SHAPES)[number];

export interface MockSticker extends Sticker {
  color: string;
  shape: Shape;
}

export interface MockPack extends Omit<PackDetail, "count" | "stickers"> {
  stickers: MockSticker[];
}

export interface MockLink extends LinkRequest {
  answer?: "confirmed" | "declined";
  notifications?: boolean;
}

export interface MockDb {
  scenario: Scenario;
  packs: MockPack[];
  devices: Device[];
  /** Codes of the desktop's «Привязать» (18·1): open the dev server with
   *  ?mock&startapp=link_demo, link_again for a computer that is already yours, or any
   *  other code for «Ссылка устарела» */
  links: MockLink[];
  /** What «Отзыв» sent in the last hour; «Лимит отзывов» starts with five already */
  feedback: FeedbackRequest[];
}

/** The service's limit: five feedbacks an hour per Telegram account (contract) */
export const FEEDBACK_PER_HOUR = 5;

const LINK_MINUTES = 10;

/** A request the desktop made `minutes` ago */
export function mockLink(code: string, minutes: number, linkedHere: boolean, now = Date.now()) {
  const requestedAt = now - minutes * 60_000;
  return {
    code,
    name: "DESKTOP-7F3K2Q",
    os: "windows",
    platform: "Windows 11",
    appVersion: "0.3.1",
    requestedAt: new Date(requestedAt).toISOString(),
    expiresAt: new Date(requestedAt + LINK_MINUTES * 60_000).toISOString(),
    linkedHere,
  } satisfies MockLink;
}

/** The first frame: a shape on a transparent 512×512 square, like a sticker's thumbnail */
export function thumbSvg(color: string, shape: Shape): string {
  const radius = shape === "circle" ? 150 : shape === "square" ? 48 : 96;
  const svg =
    `<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512">` +
    `<rect x="106" y="106" width="300" height="300" rx="${radius}" fill="${color}" ` +
    `stroke="#ffffff" stroke-width="12"/></svg>`;
  return `data:image/svg+xml,${encodeURIComponent(svg)}`;
}

function stickers(pack: string, emoji: string[][], videos: boolean, look: number): MockSticker[] {
  return emoji.map((list, i) => {
    const color = COLORS[(i + look) % COLORS.length] ?? COLORS[0];
    const shape = SHAPES[(i + look + Math.floor(i / 4)) % SHAPES.length] ?? "circle";
    const id = `${pack}-${i + 1}`;
    return {
      id,
      emoji: list,
      color,
      shape,
      thumbUrl: thumbSvg(color, shape),
      // The demo server draws these on a canvas (media.ts); «Без анимации» has none
      videoUrl: videos ? `/mock-media/${shape}/${color.slice(1)}.webm` : `/mock-media/missing.webm`,
    };
  });
}

const CALL_EMOJI = [
  ["😂", "🤣"],
  ["👍"],
  ["😴", "💤"],
  ["🙈"],
  ["🔥", "❤️", "🎉"],
  ["🤔"],
  ["😮"],
  ["👋"],
  ["🙏"],
  ["😎"],
  ["🥲"],
  ["✅"],
];

function repeat(count: number): string[][] {
  return Array.from({ length: count }, (_, i) => CALL_EMOJI[i % CALL_EMOJI.length] ?? ["🙂"]);
}

function pack(
  slug: string,
  title: string,
  kind: PackKind,
  emoji: string[][],
  updatedAt: string,
  videos: boolean,
): MockPack {
  const name = `${slug}_by_${BOT}`;
  // Each pack starts on its own colour, so the covers differ as on the artboard
  const list = stickers(name, emoji, videos, slug.length);
  return {
    name,
    title,
    kind,
    limit: LIMIT[kind],
    state: "ok",
    coverUrl: list[0]?.thumbUrl,
    updatedAt,
    stickers: list,
  };
}

export function createDb(scenario: Scenario): MockDb {
  const videos = scenario !== "no-video";
  const packs =
    scenario === "no-packs"
      ? []
      : [
          pack(
            "reactions_call",
            "Реакции с созвона",
            "stickers",
            CALL_EMOJI,
            "2026-10-17T18:20:00Z",
            videos,
          ),
          pack("cat_sofa", "Кот и диван", "stickers", repeat(5), "2026-10-15T09:00:00Z", videos),
          pack(
            "chat_emoji",
            "Эмодзи для чата",
            "emoji",
            repeat(18),
            "2026-10-12T12:00:00Z",
            videos,
          ),
          pack("summer_2026", "Лето 2026", "stickers", repeat(4), "2026-08-30T16:00:00Z", videos),
          pack(
            "best_2025",
            "Сборник 2025",
            "stickers",
            repeat(120),
            "2026-01-05T10:00:00Z",
            videos,
          ),
        ];
  if (scenario === "pack-missing") {
    packs.unshift({
      name: `first_test_by_${BOT}`,
      title: "Первый тест",
      kind: "stickers",
      limit: LIMIT.stickers,
      state: "missing",
      updatedAt: "2026-10-17T19:00:00Z",
      stickers: [],
    });
  }
  const devices: Device[] =
    scenario === "no-devices"
      ? []
      : [
          { id: "dev-1", name: "DESKTOP-7F3K2Q", os: "windows", linkedAt: "2026-10-12T08:30:00Z" },
          { id: "dev-2", name: "ubuntu-laptop", os: "linux", linkedAt: "2026-10-03T17:10:00Z" },
        ];
  const links = [mockLink("demo", 2, false), mockLink("again", 1, true)];
  const feedback: FeedbackRequest[] =
    scenario === "feedback-limit"
      ? Array.from({ length: FEEDBACK_PER_HOUR }, () => ({
          type: "idea",
          text: "Ещё одна идея",
          canReply: false,
        }))
      : [];
  return { scenario, packs, devices, links, feedback };
}

function strip(sticker: MockSticker): Sticker {
  return {
    id: sticker.id,
    emoji: sticker.emoji,
    thumbUrl: sticker.thumbUrl,
    videoUrl: sticker.videoUrl,
  };
}

export function summaryOf(mock: MockPack): Pack {
  const { stickers: list, ...rest } = mock;
  return { ...rest, count: list.length };
}

export function detailOf(mock: MockPack): PackDetail {
  return { ...summaryOf(mock), stickers: mock.stickers.map(strip) };
}
