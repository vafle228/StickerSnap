import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef } from "react";
import { useApp } from "../../../app/store";
import { isActive } from "../../../shared/api/live";
import { keys, useDownloads } from "../../../shared/api/queries";
import type { Download } from "../../../shared/api/types";
import { notify, problemText, type ToastHandle } from "../../../shared/notify";
import { t } from "../../../shared/strings/ru";
import { megabytes, percentOf, siteName } from "./format";
import {
  cancelDownload,
  fieldError,
  notifyLinkProblem,
  openLink,
  setLinkText,
  useLinkImport,
} from "./linkImport";

/** The popup of a download no import zone shows (artboard 20, section 4, form 1 of artboard 8). */
function progressToast(download: Download, cancel: () => void) {
  const { video } = download;
  const percent = percentOf(download);
  const reading = download.state === "reading";
  return {
    tone: "info" as const,
    title: t(reading ? "link.toast.reading" : "link.toast.downloading", { title: video.title }),
    description: t("link.toast.what", { site: siteName(video.site), quality: video.quality }),
    progress: reading ? 100 : percent,
    progressLabel: reading
      ? undefined
      : t("link.toast.progress", {
          done: megabytes(download.receivedBytes),
          total: megabytes(video.bytes),
          percent,
        }),
    progressVariant: "rainbow" as const,
    action: { label: t("import.cancel"), onClick: cancel },
  };
}

/** Mounted once at the root, like PublishWatch. A download goes on when the user leaves the
 *  screen: while no import zone shows it, a popup carries its progress. When it ends, the
 *  project opens by itself if the zone is in view; otherwise «Видео скачано» offers «Открыть».
 *  A failure about the link goes under the field, the rest is a popup (section 4). */
export function LinkWatch() {
  const client = useQueryClient();
  const downloads = useDownloads().data;
  const zones = useLinkImport((s) => s.zones);
  const shownId = useLinkImport((s) => s.downloadId);
  const toasts = useRef(new Map<string, ToastHandle>());
  // Downloads seen on their way: only their end is reported, not the ends of earlier sessions
  const running = useRef(new Set<string>());

  useEffect(() => {
    if (!downloads) return;
    // After F5 the zone picks up the newest download still on its way
    const newest = downloads.find(isActive);
    if (!shownId && newest && !running.current.has(newest.id)) {
      useLinkImport.setState({ downloadId: newest.id });
      return;
    }

    for (const download of downloads) {
      const { id } = download;
      const onScreen = zones > 0 && id === shownId;
      if (isActive(download)) {
        running.current.add(id);
        const toast = toasts.current.get(id);
        if (onScreen) {
          toast?.close();
          toasts.current.delete(id);
          continue;
        }
        const options = progressToast(download, () => {
          toasts.current.delete(id);
          void cancelDownload(client, id);
        });
        if (toast) toast.update(options);
        else toasts.current.set(id, notify.toast({ ...options, key: `download-${id}` }));
        continue;
      }

      if (!running.current.has(id)) continue;
      running.current.delete(id);
      toasts.current.get(id)?.close();
      toasts.current.delete(id);
      if (id === shownId) useLinkImport.setState({ downloadId: undefined });
      ended(download, onScreen);
    }

    function ended(download: Download, onScreen: boolean) {
      const { video } = download;
      if (download.state === "succeeded" && download.projectId) {
        const projectId = download.projectId;
        void client.invalidateQueries({ queryKey: keys.projects });
        if (onScreen) {
          setLinkText("");
          useApp.getState().openProject(projectId);
          return;
        }
        notify.toast({
          tone: "success",
          title: t("link.toast.done"),
          description: t("link.toast.doneText", { title: video.title }),
          action: {
            label: t("link.open"),
            onClick: () => useApp.getState().openProject(projectId),
          },
        });
        return;
      }
      if (download.state !== "failed" || !download.error) return;
      const problem = download.error;
      const retry = () => void openLink(client, video.url);
      const hint = fieldError(problem);
      if (!hint) {
        notifyLinkProblem(problem, retry);
        return;
      }
      // About the link itself: under the field, with the link back in it for a fix (3·7)
      useLinkImport.setState({ text: video.url, error: hint });
      if (!onScreen) {
        notify.toast({
          key: "link",
          tone: "danger",
          title: hint,
          description: problemText(problem).text,
        });
      }
    }
  }, [downloads, zones, shownId, client]);

  return null;
}
