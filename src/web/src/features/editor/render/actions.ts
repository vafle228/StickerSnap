import type { QueryClient } from "@tanstack/react-query";
import { api, call } from "../../../shared/api/client";
import { applyJob } from "../../../shared/api/live";
import type { Segment } from "../../../shared/api/types";
import { notifyProblem } from "../../../shared/notify";
import { flushSave } from "../model/autosave";
import { useEditor } from "../model/store";
import { signature, useRender } from "./store";

/** «Экспорт WEBM» or «Экспортировать все отрезки»: one job for the segments given; its
 *  progress and result come as jobUpdated (useRenderJobs shows them). */
export async function exportSegments(client: QueryClient, segmentIds: string[]) {
  const { projectId } = useEditor.getState();
  if (!projectId || segmentIds.length === 0 || useRender.getState().exporting) return;
  try {
    // Export goes by a saved revision (contract, «Фоновые задачи»): autosave has mostly done it.
    // A scene that cannot be saved is already told by autosave's popup and «Не сохранено».
    const revision = await flushSave().catch(() => undefined);
    if (revision === undefined) return;
    const job = await call(
      api.POST("/api/v1/jobs", { body: { kind: "export", projectId, revision, segmentIds } }),
    );
    applyJob(client, job);
    useRender.setState({ exporting: { jobId: job.id, segmentIds } });
  } catch (error) {
    notifyProblem(error, { key: "export" });
  }
}

/** «Отменить» of an export, or × of the draft. */
export async function cancelJob(client: QueryClient, jobId: string) {
  try {
    applyJob(
      client,
      await call(api.POST("/api/v1/jobs/{jobId}/cancel", { params: { path: { jobId } } })),
    );
  } catch (error) {
    notifyProblem(error, { key: "job-cancel" });
  }
}

/** A new draft of the segment as it is now; the server cancels the previous one (3c, D). */
export async function startPreview(client: QueryClient, segment: Segment) {
  const { projectId } = useEditor.getState();
  if (!projectId) return;
  const of = signature(segment);
  useRender.setState({ preview: { jobId: "", of } });
  try {
    const job = await call(
      api.POST("/api/v1/jobs", { body: { kind: "preview", projectId, segment } }),
    );
    applyJob(client, job);
    // An edit made while the request was on its way has already asked for a newer draft.
    if (useRender.getState().preview?.of === of) {
      useRender.setState({ preview: { jobId: job.id, of } });
    } else {
      void cancelJob(client, job.id);
    }
  } catch (error) {
    useRender.getState().setExact(false);
    notifyProblem(error, { key: "preview" });
  }
}
