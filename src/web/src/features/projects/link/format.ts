import type { Download, LinkSite, LinkVideo } from "../../../shared/api/types";
import { t } from "../../../shared/strings/ru";
import { num } from "../../editor/format";

/* The words of a download by link (artboard 20, 3·5 and section 4). */

/** Longer than this a video is downloaded anyway, with «Ролик длинный» in its card (decided
 *  24 October: no limit, a warning instead). */
export const LONG_VIDEO_SEC = 600;

export const isLong = (video: LinkVideo) => video.durationSec > LONG_VIDEO_SEC;

const SITES: Record<LinkSite, string> = { youtube: "YouTube", giphy: "Giphy" };

/** "youtube" → «YouTube»; a site the SPA does not know yet is shown as the server names it. */
export function siteName(site: string | number | undefined): string {
  return SITES[site as LinkSite] ?? String(site ?? "");
}

/** 48 → «0:48», 4360 → «1:12:40». */
export function duration(sec: number): string {
  const total = Math.max(0, Math.round(sec));
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = String(total % 60).padStart(2, "0");
  return h > 0 ? `${h}:${String(m).padStart(2, "0")}:${s}` : `${m}:${s}`;
}

/** Megabytes the way the card counts them: 13 of 38, «0,4» below one. */
export function megabytes(bytes: number): string {
  const mb = bytes / 1024 / 1024;
  return mb < 1 ? num(mb, 1) : num(Math.round(mb));
}

/** «ещё ~20 с», «ещё ~3 мин», «ещё ~1 ч 10 мин». */
export function eta(sec: number): string {
  const total = Math.max(1, Math.round(sec));
  if (total < 60) return t("link.eta.seconds", { n: total });
  const minutes = Math.round(total / 60);
  if (minutes < 60) return t("link.eta.minutes", { n: minutes });
  return t("link.eta.hours", { h: Math.floor(minutes / 60), m: minutes % 60 });
}

/** 0–100 of the bytes so far. */
export function percentOf(download: Download): number {
  const total = download.video.bytes;
  return total > 0 ? Math.min(100, Math.round((download.receivedBytes / total) * 100)) : 0;
}

/** «13 из 38 МБ». */
export function amount(download: Download): string {
  return t("link.amount", {
    done: megabytes(download.receivedBytes),
    total: megabytes(download.video.bytes),
  });
}

/** «YouTube · 0:48 · скачиваю 720p» under the title. */
export function downloadMeta(download: Download): string {
  const { video } = download;
  return t("link.meta", {
    site: siteName(video.site),
    duration: duration(video.durationSec),
    what:
      download.state === "queued"
        ? t("link.queued")
        : t("link.downloading", { quality: video.quality }),
  });
}

/** The yellow line of a long video: its length and weight, and that it takes a while. */
export function longNote(video: LinkVideo): string {
  return t("link.long", { duration: duration(video.durationSec), size: megabytes(video.bytes) });
}
