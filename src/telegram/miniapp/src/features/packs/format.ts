import type { Pack } from "../../api/types";
import { plural } from "../../shared/plural";

/* The words of the pack screens (artboards 13–14). */

const STICKERS = ["стикер", "стикера", "стикеров"] as const;

export function isFull(pack: Pick<Pack, "count" | "limit">): boolean {
  return pack.count >= pack.limit;
}

/** «Стикеры · 12 из 120», «Эмодзи · 18 из 200» */
export function countLabel(pack: Pick<Pack, "kind" | "count" | "limit">): string {
  return `${pack.kind === "emoji" ? "Эмодзи" : "Стикеры"} · ${pack.count} из ${pack.limit}`;
}

/** Above the grid: «12 стикеров», a full pack also says why nothing more goes in */
export function gridLabel(pack: Pick<Pack, "count" | "limit">): string {
  const count = `${pack.count} ${plural(pack.count, STICKERS)}`;
  return isFull(pack) ? `${count} · больше Telegram не примет` : count;
}

/** «В паке осталось 11 стикеров.» after a delete */
export function leftLabel(count: number): string {
  const left = plural(count, ["остался", "осталось", "осталось"]);
  return `В паке ${left} ${count} ${plural(count, STICKERS)}.`;
}

/** The pack's own link: Telegram opens its «add» window on it */
export function packLink(pack: Pick<Pack, "kind" | "name">): string {
  return `t.me/${pack.kind === "emoji" ? "addemoji" : "addstickers"}/${pack.name}`;
}

/** Telegram's chat picker with the link of the pack */
export function shareUrl(pack: Pick<Pack, "kind" | "name" | "title">): string {
  const url = encodeURIComponent(`https://${packLink(pack)}`);
  return `https://t.me/share/url?url=${url}&text=${encodeURIComponent(pack.title)}`;
}
