import type { LinkSite } from "../../../shared/api/types";

/* What a pasted link points to, told by its look alone (artboard 20, section 5): the field's
   source badge shows at once, without a request. The server decides for good: a channel or a
   private video looks like any other link here. */

/** A gif or a sticker of Giphy: a sticker keeps its transparency, so it comes as WEBP. */
export type LinkKind = "youtube" | "giphy-gif" | "giphy-sticker";

export type LinkCheck =
  { ok: true; site: LinkSite; kind: LinkKind } | { ok: false; reason: "not_link" | "site" };

const YOUTUBE = new Set(["youtube.com", "m.youtube.com", "music.youtube.com", "youtu.be"]);

/** The address in the text, with https:// added when a link was copied without it. */
function urlOf(text: string): URL | undefined {
  const trimmed = text.trim();
  if (!trimmed || /\s/.test(trimmed)) return undefined;
  const withScheme = /^[a-z][a-z0-9+.-]*:\/\//i.test(trimmed) ? trimmed : `https://${trimmed}`;
  try {
    const url = new URL(withScheme);
    if (url.protocol !== "https:" && url.protocol !== "http:") return undefined;
    // A word without a dot is not a site: «котик» stays «Это не ссылка»
    return url.hostname.includes(".") ? url : undefined;
  } catch {
    return undefined;
  }
}

function kindOf(url: URL): LinkKind | undefined {
  const host = url.hostname.toLowerCase().replace(/^www\./, "");
  if (YOUTUBE.has(host)) return "youtube";
  if (host === "giphy.com") {
    if (url.pathname.startsWith("/stickers/")) return "giphy-sticker";
    return url.pathname.startsWith("/gifs/") || url.pathname.startsWith("/clips/")
      ? "giphy-gif"
      : undefined;
  }
  // media.giphy.com, media0–4.giphy.com, i.giphy.com: a file of a gif
  if (host === "i.giphy.com" || /^media\d?\.giphy\.com$/.test(host)) return "giphy-gif";
  return undefined;
}

/** Checked at «Открыть», not on every letter: «Это не ссылка» or «Пока умею только YouTube и Giphy». */
export function checkLink(text: string): LinkCheck {
  const url = urlOf(text);
  if (!url) return { ok: false, reason: "not_link" };
  const kind = kindOf(url);
  if (!kind) return { ok: false, reason: "site" };
  return { ok: true, site: kind === "youtube" ? "youtube" : "giphy", kind };
}

/** The source badge in the field while typing: only a link we take gets one. */
export function recognize(text: string): Extract<LinkCheck, { ok: true }> | undefined {
  const check = checkLink(text);
  return check.ok ? check : undefined;
}

/** Text that is an address at all: Ctrl+V of anything else is left to the page. */
export function looksLikeLink(text: string): boolean {
  return urlOf(text) !== undefined;
}

/** «youtube.com/watch?v=[id]»: the link under «Узнаю о видео…», without the scheme and www. */
export function shortLink(text: string): string {
  const url = urlOf(text);
  if (!url) return text.trim();
  return `${url.hostname.replace(/^www\./, "")}${url.pathname}${url.search}`.replace(/\/$/, "");
}
