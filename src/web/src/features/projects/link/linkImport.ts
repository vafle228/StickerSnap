import { useQueryClient, type QueryClient } from "@tanstack/react-query";
import { useEffect, useMemo } from "react";
import { create } from "zustand";
import { useLatest } from "@stickersnap/ds/hooks";
import { api, ApiError, call } from "../../../shared/api/client";
import { applyDownload } from "../../../shared/api/live";
import type { Problem } from "../../../shared/api/types";
import { notify, notifyProblem, PROBLEMS, toProblem } from "../../../shared/notify";
import { t } from "../../../shared/strings/ru";
import { megabytes, siteName } from "./format";
import { checkLink, looksLikeLink } from "./parse";

/* Import by link (artboard 20): the field, «Узнаю о видео…» and the download the zone shows.
   One state for the start screen's field and the tile's popover, so a link typed in one is
   there in the other and stays after «Отменить» (3·9). The downloads themselves live on the
   server and in the query cache (GET /downloads, downloadUpdated). */

export interface LinkState {
  /** What is in the field */
  text: string;
  /** The hint under the field: what is wrong with this link (3·3, 3·7) */
  error?: string;
  /** POST /links/probe is on (3·4); the card shows only after 300 ms, as «Читаю файл» does */
  probing?: { url: string; slow: boolean; abort: AbortController };
  /** The download the import zone shows: the one started last here */
  downloadId?: string;
  /** Import zones on screen now (start screen, the tile's popover). With none, a running
   *  download reports by popup (section 4) */
  zones: number;
}

export const useLinkImport = create<LinkState>()(() => ({ text: "", zones: 0 }));

const SLOW_MS = 300;

export function setLinkText(text: string) {
  useLinkImport.setState({ text, error: undefined });
}

/** Counts an import zone on screen while the calling component is mounted. */
export function useLinkZone(shown = true) {
  useEffect(() => {
    if (!shown) return undefined;
    useLinkImport.setState((s) => ({ zones: s.zones + 1 }));
    return () => useLinkImport.setState((s) => ({ zones: s.zones - 1 }));
  }, [shown]);
}

const typingIn = (target: EventTarget | null) =>
  target instanceof HTMLElement &&
  (target.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(target.tagName));

/** Ctrl+V of a link anywhere on the screen, outside a text field, opens it (artboard 20·1).
 *  Any other text is left to the page. */
export function useLinkPaste(enabled: boolean, onLink: (link: string) => void) {
  const handler = useLatest(onLink);
  useEffect(() => {
    if (!enabled) return undefined;
    const paste = (e: ClipboardEvent) => {
      if (typingIn(e.target)) return;
      const text = e.clipboardData?.getData("text") ?? "";
      if (!looksLikeLink(text)) return;
      e.preventDefault();
      handler.current(text.trim());
    };
    document.addEventListener("paste", paste);
    return () => document.removeEventListener("paste", paste);
  }, [enabled, handler]);
}

const REASONS: Record<string, Parameters<typeof t>[0]> = {
  not_link: "link.notLink",
  site: "link.site",
  gone: "link.gone",
  live: "link.live",
  adult: "link.adult",
  playlist: "link.playlist",
};

/** The hint under the field for a problem with the link itself; undefined for the rest. */
export function fieldError(problem: Problem): string | undefined {
  if (PROBLEMS[problem.code].form !== "field") return undefined;
  const reason = REASONS[String(problem.params?.reason ?? "")];
  if (reason) return t(reason);
  return problem.code === "link_unsupported" ? t("link.site") : t("link.gone");
}

/** The problem with its site named as people write it: «youtube» → «YouTube». */
function named(problem: Problem): Problem {
  const site = problem.params?.site;
  return site === undefined
    ? problem
    : { ...problem, params: { ...problem.params, site: siteName(site) } };
}

/** A failure that is not about the link (section 4): no network, the downloader broken, no
 *  room. `retry` starts it over: «Повторить» after a lost network. */
export function notifyLinkProblem(problem: Problem, retry: () => void) {
  if (problem.code === "disk_full") {
    const need = Number(problem.params?.needBytes ?? 0);
    const free = Number(problem.params?.freeBytes ?? 0);
    notify.toast({
      key: "link",
      tone: "danger",
      title: t("link.diskFull.title"),
      description: t("link.diskFull.text", { need: megabytes(need), free: megabytes(free) }),
      details: { log: problem.log, traceId: problem.traceId || undefined },
    });
    return;
  }
  notifyProblem(new ApiError(problem.status, named(problem)), {
    key: "link",
    action:
      problem.code === "site_unreachable"
        ? { label: t("common.retry"), onClick: retry }
        : undefined,
  });
}

/** What a failed probe or download says: under the field when it is about the link, else a popup. */
function failed(problem: Problem, retry: () => void) {
  const hint = fieldError(problem);
  if (hint) useLinkImport.setState({ error: hint });
  else notifyLinkProblem(problem, retry);
}

const isAbort = (error: unknown) => error instanceof DOMException && error.name === "AbortError";

/** «Открыть», Enter, Ctrl+V or a dropped link: checks the look of the link, asks the server what
 *  the video is (3·4), then starts its download (3·5). `text` replaces the field's text. */
export async function openLink(client: QueryClient, text?: string) {
  if (text !== undefined) setLinkText(text.trim());
  const { text: link, probing } = useLinkImport.getState();
  const url = link.trim();
  if (!url || probing) return;
  const check = checkLink(url);
  if (!check.ok) {
    useLinkImport.setState({
      error: t(check.reason === "not_link" ? "link.notLink" : "link.site"),
    });
    return;
  }

  const abort = new AbortController();
  useLinkImport.setState({ error: undefined, probing: { url, slow: false, abort } });
  const timer = setTimeout(() => {
    const now = useLinkImport.getState().probing;
    if (now?.abort === abort) useLinkImport.setState({ probing: { ...now, slow: true } });
  }, SLOW_MS);
  try {
    const video = await call(
      api.POST("/api/v1/links/probe", { body: { url }, signal: abort.signal }),
    );
    const download = await call(
      api.POST("/api/v1/downloads", { body: { url: video.url }, signal: abort.signal }),
    );
    applyDownload(client, download);
    useLinkImport.setState({ downloadId: download.id });
  } catch (error) {
    if (!abort.signal.aborted && !isAbort(error)) {
      failed(toProblem(error), () => void openLink(client, url));
    }
  } finally {
    clearTimeout(timer);
    if (useLinkImport.getState().probing?.abort === abort)
      useLinkImport.setState({ probing: undefined });
  }
}

/** «Отменить» of a download: the server drops the partial file. */
export async function cancelDownload(client: QueryClient, id: string) {
  if (useLinkImport.getState().downloadId === id) useLinkImport.setState({ downloadId: undefined });
  try {
    applyDownload(
      client,
      await call(
        api.POST("/api/v1/downloads/{downloadId}/cancel", { params: { path: { downloadId: id } } }),
      ),
    );
  } catch (error) {
    notifyProblem(error, { key: "link" });
  }
}

/** «Отменить» on any step (3·9): the zone comes back with the link still in the field. */
export async function cancelLink(client: QueryClient) {
  const { probing, downloadId } = useLinkImport.getState();
  if (probing) {
    probing.abort.abort();
    useLinkImport.setState({ probing: undefined });
  } else if (downloadId) {
    await cancelDownload(client, downloadId);
  }
}

export function useLinkActions() {
  const client = useQueryClient();
  return useMemo(
    () => ({
      open: (text?: string) => openLink(client, text),
      cancel: () => cancelLink(client),
      cancelDownload: (id: string) => cancelDownload(client, id),
    }),
    [client],
  );
}
