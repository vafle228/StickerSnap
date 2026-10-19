import { useQueryClient, type QueryClient } from "@tanstack/react-query";
import { useMemo } from "react";
import { useApp } from "../../app/store";
import { api, call } from "../../shared/api/client";
import { keys } from "../../shared/api/queries";
import type { Project, ProjectSummary, SourceRef } from "../../shared/api/types";
import { confirm, notify, notifyProblem, toProblem } from "../../shared/notify";
import { platform, type LocalFile } from "../../shared/platform";
import { captureProblem } from "../../shared/report";
import { t } from "../../shared/strings/ru";
import { useEditor } from "../editor/model/store";
import { checkWebm } from "../validator/check";
import { readFile, useImport } from "./reading";

/** A video that cannot become a project (artboard 3c, B and C): the popup names the file and
 *  offers another one. The open project and the start screen stay as they were. */
function notifyImportProblem(error: unknown, file: LocalFile, pickAnother: () => void) {
  const problem = toProblem(error);
  const another = { label: t("import.another"), onClick: pickAnother };
  if (problem.code === "source_unreadable") {
    const title = t("import.unreadable.title", { name: file.name });
    notify.toast({
      key: "import",
      tone: "danger",
      title,
      description: t("import.unreadable.text"),
      action: another,
      details: { log: problem.log, traceId: problem.traceId || undefined },
      report: captureProblem(problem, title),
    });
  } else if (problem.code === "format_unsupported") {
    const ext = problem.params?.ext ?? file.name.split(".").pop() ?? "";
    notify.toast({
      key: "import",
      tone: "warning",
      title: t("import.unsupported.title", { ext }),
      description: t("import.unsupported.text"),
      action: another,
    });
  } else {
    notifyProblem(error, { key: "import" });
  }
}

/** A .webm dropped on the window is a finished sticker to check (R8); anything else is a video. */
export const isWebm = (name: string) => /\.webm$/i.test(name);

type Summary = Pick<ProjectSummary, "id" | "name" | "path">;

function dropFromList(client: QueryClient, id: string) {
  client.setQueryData<ProjectSummary[]>(keys.projects, (list) => list?.filter((p) => p.id !== id));
}

/** The project's place in the editor is gone: the start screen takes the canvas back. */
function closeIfOpen(id: string) {
  const app = useApp.getState();
  if (app.projectId === id) app.closeProject();
}

/** Picks a segment in the editor as soon as its project is there; it may still be loading. */
export function selectOnceLoaded(projectId: string, segmentId: string) {
  if (useEditor.getState().projectId === projectId) {
    useEditor.getState().select(segmentId);
    return;
  }
  const stop = useEditor.subscribe((s) => {
    if (s.projectId !== projectId) return;
    stop();
    s.select(segmentId);
  });
}

/** What «Проекты» and the start screen do with projects (artboard 6). Every action reports its
 *  own failure as the popup of artboard 8; none throws. */
export function useProjectActions() {
  const client = useQueryClient();

  return useMemo(() => {
    const opened = (project: Project) => {
      client.setQueryData(keys.project(project.id), project);
      void client.invalidateQueries({ queryKey: keys.projects });
      useApp.getState().openProject(project.id);
    };

    /** «Новый проект», «Импорт видео»: a picked or dropped video becomes a project and opens in
     *  the editor. `source` skips the upload when the server already has the file. */
    const create = async (picked?: LocalFile, source?: SourceRef) => {
      if (useImport.getState().reading) return;
      const file = picked ?? (await platform.pickVideo());
      if (!file) return;
      const result = await readFile(file, "video", async (signal) => {
        const body = { source: source ?? (await platform.sourceOf(file, signal)) };
        return call(api.POST("/api/v1/projects", { body, signal }));
      });
      if (!result) return;
      if ("error" in result) notifyImportProblem(result.error, file, () => void create());
      else opened(result.value);
    };

    /** A file dropped on the window or an import zone. */
    const openDropped = (dropped: File) => {
      const file = platform.dropped(dropped);
      if (isWebm(file.name)) void checkWebm(file, (source) => void create(file, source));
      else void create(file);
    };

    /** «Открыть файл…»: a .ssproj from disk, desktop only. */
    const openFile = async () => {
      const path = await platform.pickProject();
      if (!path) return;
      try {
        opened(await call(api.POST("/api/v1/projects/open", { body: { path } })));
      } catch (error) {
        notifyProblem(error, { key: "project-open" });
      }
    };

    const rename = async (project: Summary, wanted: string) => {
      const name = wanted.trim();
      if (!name || name === project.name) return;
      try {
        const summary = await call(
          api.PATCH("/api/v1/projects/{id}", {
            params: { path: { id: project.id } },
            body: { name },
          }),
        );
        client.setQueryData<ProjectSummary[]>(keys.projects, (list) =>
          list?.map((p) => (p.id === summary.id ? summary : p)),
        );
        client.setQueryData<Project>(keys.project(summary.id), (p) => p && { ...p, name });
        if (useEditor.getState().projectId === summary.id) useEditor.setState({ name });
      } catch (error) {
        // A rename is not retried like an autosave: say the old name stayed.
        const problem = toProblem(error);
        if (problem.code !== "save_failed") {
          notifyProblem(error, { key: "project-rename" });
          return;
        }
        notify.toast({
          key: "project-rename",
          tone: "danger",
          title: t("error.save_failed.title"),
          description: t("projects.renameFailed"),
          details: { log: problem.log, traceId: problem.traceId },
        });
      }
    };

    /** «Убрать из списка» acts at once and can be undone: the file stays on disk. */
    const forget = async (project: Summary) => {
      try {
        await call(
          api.DELETE("/api/v1/projects/{id}", {
            params: { path: { id: project.id }, query: { mode: "forget" } },
          }),
        );
      } catch (error) {
        notifyProblem(error, { key: "project-forget" });
        return;
      }
      dropFromList(client, project.id);
      closeIfOpen(project.id);
      const path = project.path;
      notify.toast({
        tone: "info",
        title: t("projects.forgotten.title"),
        description: t("projects.forgotten.text", { name: project.name }),
        action: path
          ? { label: t("projects.restore"), onClick: () => void restore(path) }
          : undefined,
      });
    };

    /** «Вернуть»: the same request as opening the file by its path (contract). */
    const restore = async (path: string) => {
      try {
        await call(api.POST("/api/v1/projects/open", { body: { path } }));
        await client.invalidateQueries({ queryKey: keys.projects });
      } catch (error) {
        notifyProblem(error, { key: "project-restore" });
      }
    };

    /** «Удалить с диска…» asks first: the .ssproj goes to the trash. */
    const trash = async (project: Summary) => {
      const yes = await confirm({
        title: t("projects.trash.title", { name: project.name }),
        text: t("projects.trash.text"),
        confirmLabel: t("projects.trash.confirm"),
        icon: "trash",
      });
      if (!yes) return;
      try {
        await call(
          api.DELETE("/api/v1/projects/{id}", {
            params: { path: { id: project.id }, query: { mode: "trash" } },
          }),
        );
        dropFromList(client, project.id);
        closeIfOpen(project.id);
        client.removeQueries({ queryKey: keys.project(project.id) });
      } catch (error) {
        notifyProblem(error, { key: "project-trash" });
      }
    };

    /** «Указать исходник»: a new video for a project whose source is gone. Segments past the
     *  end of a shorter video stay, with a warning that leads to them. */
    const replaceSource = async (id: string) => {
      try {
        const file = await platform.pickVideo();
        if (!file) return;
        const source = await platform.sourceOf(file);
        const { project, segmentsOutOfRange } = await call(
          api.PUT("/api/v1/projects/{id}/source", { params: { path: { id } }, body: { source } }),
        );
        client.setQueryData(keys.project(id), project);
        void client.invalidateQueries({ queryKey: keys.projects });
        if (useEditor.getState().projectId === id) {
          useEditor.setState({ source: project.source, mediaUrl: project.mediaUrl });
        }
        const out = project.scene.segments.filter((s) => segmentsOutOfRange.includes(s.id));
        if (out.length === 0) return;
        notify.toast({
          tone: "warning",
          title:
            out.length === 1
              ? t("projects.outOfRange.one", { name: out[0]?.name ?? "" })
              : t("projects.outOfRange.many", { names: out.map((s) => s.name).join(", ") }),
          description: t("projects.outOfRange.text"),
          action: {
            label: t("projects.show"),
            onClick: () => {
              useApp.getState().openProject(id);
              if (out[0]) selectOnceLoaded(id, out[0].id);
            },
          },
        });
      } catch (error) {
        notifyProblem(error, { key: "project-source" });
      }
    };

    return { create, openDropped, openFile, rename, forget, trash, replaceSource };
  }, [client]);
}
