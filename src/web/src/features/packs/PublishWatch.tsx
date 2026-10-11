import { useEffect, useMemo, useRef, useState } from "react";
import { useJobs, usePacks } from "../../shared/api/queries";
import type { Job } from "../../shared/api/types";
import { notify, type ToastHandle } from "../../shared/notify";
import { t } from "../../shared/strings/ru";
import { usePackActions } from "./actions";
import { countdown } from "./format";

const running = (j: Job) => j.kind === "publish" && (j.state === "running" || j.state === "queued");

/** The send popup of artboard 12: lives while a publish job runs, wherever the user is. When
 *  Telegram asks to wait it turns yellow with the countdown and «Повторить сейчас» (7-6). */
export function PublishWatch() {
  const jobs = useJobs().data;
  const packs = usePacks().data;
  const { retry } = usePackActions();
  const toasts = useRef(new Map<string, ToastHandle>());
  const [now, setNow] = useState(Date.now());
  const active = useMemo(() => (jobs ?? []).filter(running), [jobs]);
  const waiting = active.some((j) => j.progress.retryAt);

  useEffect(() => {
    if (!waiting) return undefined;
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [waiting]);

  useEffect(() => {
    const live = new Set(active.map((j) => j.id));
    for (const [id, toast] of toasts.current) {
      if (!live.has(id)) {
        toast.close();
        toasts.current.delete(id);
      }
    }
    for (const job of active) {
      const total = job.items.length;
      const done = job.items.filter((i) => i.state === "done").length;
      const title = packs?.find((p) => p.name === job.pack)?.title ?? "";
      const retryAt = job.progress.retryAt;
      const options = retryAt
        ? {
            tone: "warning" as const,
            title: t("packs.wait.title", { done, total, left: total - done }),
            description: t("packs.wait.text", {
              time: countdown(new Date(retryAt).getTime() - now),
            }),
            action: {
              label: t("packs.wait.retry"),
              // Pressing closes the toast: the next update brings the send popup back
              onClick: () => {
                toasts.current.delete(job.id);
                void retry(job.id);
              },
            },
            progress: undefined,
          }
        : {
            tone: "info" as const,
            title: t("packs.send.title", { done, total, pack: title }),
            description: undefined,
            action: undefined,
            progress: Math.round(job.progress.fraction * 100),
            progressLabel: t("packs.send.label"),
            progressVariant: "rainbow" as const,
          };
      const toast = toasts.current.get(job.id);
      if (toast) toast.update(options);
      else toasts.current.set(job.id, notify.toast({ ...options, key: `publish-${job.id}` }));
    }
  }, [active, packs, now, retry]);

  return null;
}
