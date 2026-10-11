import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef } from "react";
import { ApiError, fetchFile } from "../../../shared/api/client";
import { keys, useAppInfo, useJob, useJobs } from "../../../shared/api/queries";
import type { ExportResult, Job, Project } from "../../../shared/api/types";
import { notify, notifyProblem, type ToastHandle } from "../../../shared/notify";
import { platform } from "../../../shared/platform";
import { t } from "../../../shared/strings/ru";
import { useEditor, useSegment } from "../model/store";
import { useDebounced } from "../useDebounced";
import { cancelJob, exportSegments, startPreview } from "./actions";
import { setPreviewFile, signature, useRender } from "./store";
import { exportToast } from "./toast";

/** An edit settles this long before a new draft is asked for. */
const DRAFT_DELAY_MS = 500;

const running = (job: Job) => job.state === "queued" || job.state === "running";

/** Mounted with the editor: follows the export and the draft of the open project. */
export function useRenderJobs(project: Project | undefined) {
  useExportJob(project);
  useExactPreview();
}

/** The export's popup, the downloads of the browser (R9) and the results kept for the
 *  inspector. A job still running after F5 is picked up from GET /api/v1/jobs. */
function useExportJob(project: Project | undefined) {
  const client = useQueryClient();
  const projectId = useEditor((s) => s.projectId);
  const exporting = useRender((s) => s.exporting);
  const jobs = useJobs().data;
  const job = useJob(exporting?.jobId).data;
  const desktop = useAppInfo().data?.mode === "desktop";
  const toast = useRef<{ jobId: string; handle?: ToastHandle; dismissed: boolean }>();
  const finished = useRef(new Set<string>());
  const loaded = useRef<string>();

  // A new project: its last exports come from the server, still fresh unless edited since.
  useEffect(() => {
    if (!projectId) loaded.current = undefined;
    if (!project || project.id !== projectId || loaded.current === projectId) return;
    loaded.current = projectId;
    const results: ReturnType<typeof useRender.getState>["results"] = {};
    for (const [id, result] of Object.entries(project.exports)) {
      const segment = project.scene.segments.find((s) => s.id === id);
      if (segment && !result.stale) results[id] = { result, of: signature(segment) };
    }
    useRender.getState().reset(results);
  }, [project, projectId]);

  // After F5: an export of this project that is still running gets its popup back.
  useEffect(() => {
    if (!projectId || useRender.getState().exporting) return;
    const found = jobs?.find((j) => j.kind === "export" && j.projectId === projectId && running(j));
    if (found) {
      useRender.setState({
        exporting: { jobId: found.id, segmentIds: found.items.map((i) => i.segmentId) },
      });
    }
  }, [jobs, projectId]);

  useEffect(() => {
    if (!job || job.kind !== "export") return;
    if (toast.current?.jobId !== job.id) toast.current = { jobId: job.id, dismissed: false };
    const shown = toast.current;
    const segments = useEditor.getState().scene?.segments ?? [];
    const content = exportToast(job, segments);
    const done = !running(job);

    if (job.state === "canceled" || !content) {
      shown.handle?.close();
    } else if (!shown.dismissed || done) {
      // Closed while it ran: the result still comes, as a new popup (artboard 8).
      const first = job.items.find((i) => i.export)?.export;
      const action = running(job)
        ? { label: t("export.toastCancel"), onClick: () => void cancelJob(client, job.id) }
        : job.state === "failed"
          ? {
              label: t("common.retry"),
              onClick: () =>
                void exportSegments(
                  client,
                  job.items.map((i) => i.segmentId),
                ),
            }
          : desktop && first?.path
            ? {
                label: t("export.inFolder"),
                onClick: () => platform.showInFolder(first.path ?? ""),
              }
            : undefined;
      shown.handle = notify.toast({
        ...content,
        key: `export-${job.id}`,
        action,
        // The finished popup lives as the tone says; a running one stays until its result.
        timeout: done ? undefined : null,
        onClose: () => {
          shown.dismissed = true;
        },
      });
    }
    if (!done || finished.current.has(job.id)) return;
    finished.current.add(job.id);

    useRender.setState({ exporting: undefined });
    if (job.state !== "succeeded" && job.state !== "partial") return;
    const scene = useEditor.getState().scene;
    const results = { ...useRender.getState().results };
    const exported: [string, ExportResult][] = [];
    for (const item of job.items) {
      const result = item.export;
      const segment = scene?.segments.find((s) => s.id === item.segmentId);
      if (!result || !segment) continue;
      results[segment.id] = { result, of: signature(segment) };
      exported.push([segment.id, result]);
      // The browser has no folder: the finished file downloads (R9).
      if (!desktop) {
        platform
          .saveFile(result.fileUrl, `${segment.name}.webm`)
          .catch((error: unknown) => notifyProblem(error, { key: "export-save" }));
      }
    }
    useRender.setState({ results });
    // The project as the server now has it: its exports survive the editor's remount.
    client.setQueryData<Project>(keys.project(job.projectId), (p) =>
      p ? { ...p, exports: { ...p.exports, ...Object.fromEntries(exported) } } : p,
    );
    void client.invalidateQueries({ queryKey: keys.projects });
  }, [job, client, desktop]);
}

/** «Точное превью» (3c, D): while it is on, every settled edit asks for a new draft; the
 *  finished one is fetched and played from memory. */
function useExactPreview() {
  const client = useQueryClient();
  const exact = useRender((s) => s.exact);
  const preview = useRender((s) => s.preview);
  const segment = useSegment();
  const settled = useDebounced(segment, DRAFT_DELAY_MS);
  const job = useJob(preview?.jobId || undefined).data;

  useEffect(() => {
    if (!exact || !settled) return;
    const of = signature(settled);
    const { preview: asked, previewFile } = useRender.getState();
    if (asked?.of === of || previewFile?.of === of) return;
    void startPreview(client, settled);
  }, [exact, settled, client]);

  useEffect(() => {
    if (!job || job.kind !== "preview" || job.id !== preview?.jobId) return;
    if (job.state === "failed") {
      useRender.getState().setExact(false);
      if (job.error) notifyProblem(new ApiError(job.error.status, job.error), { key: "preview" });
      return;
    }
    const file = job.items[0]?.preview;
    if (job.state !== "succeeded" || !file) return;
    const abort = new AbortController();
    fetchFile(file.fileUrl, abort.signal).then(
      (blob) => {
        if (useRender.getState().preview?.jobId !== job.id) return;
        setPreviewFile({ url: URL.createObjectURL(blob), bytes: file.bytes, of: preview.of });
        useRender.setState({ preview: undefined });
      },
      (error: unknown) => {
        if (abort.signal.aborted) return;
        useRender.getState().setExact(false);
        notifyProblem(error, { key: "preview" });
      },
    );
    return () => abort.abort();
  }, [job, preview]);
}
