import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { notifyProblem } from "../notify/problems";
import { api, call } from "./client";
import type { AccountLink, FeedbackRequest, Segment, Settings, SettingsPatch } from "./types";

/** Query keys of the server's resources. Lists and items have their own roots, so refetching
 *  a list after a reconnect never refetches the project open in the editor. */
export const keys = {
  app: ["app"],
  settings: ["settings"],
  projects: ["projects"],
  project: (id: string) => ["project", id],
  jobs: ["jobs"],
  job: (id: string) => ["job", id],
  account: ["account"],
  packs: ["packs"],
  pack: (name: string) => ["pack", name],
  downloads: ["downloads"],
} as const;

export function useAppInfo() {
  return useQuery({
    queryKey: keys.app,
    queryFn: () => call(api.GET("/api/v1/app")),
    staleTime: Infinity,
  });
}

export function useSettings() {
  return useQuery({
    queryKey: keys.settings,
    queryFn: () => call(api.GET("/api/v1/settings")),
    // settingsChanged keeps it fresh
    staleTime: Infinity,
  });
}

/** PATCH /settings right away, without «Сохранить»: the cache changes first, a failure rolls it back. */
export function useUpdateSettings() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (patch: SettingsPatch) => call(api.PATCH("/api/v1/settings", { body: patch })),
    onMutate: (patch) => {
      const before = client.getQueryData<Settings>(keys.settings);
      if (before) {
        client.setQueryData<Settings>(keys.settings, {
          ...before,
          ...patch,
          folders: { ...before.folders, ...patch.folders },
        });
      }
      return { before };
    },
    onError: (error, _patch, context) => {
      if (context?.before) client.setQueryData(keys.settings, context.before);
      notifyProblem(error, { key: "settings" });
    },
    onSuccess: (settings) => client.setQueryData(keys.settings, settings),
  });
}

/** The Telegram account link (artboard 5); accountChanged keeps it fresh. */
export function useAccount() {
  return useQuery({
    queryKey: keys.account,
    queryFn: () => call(api.GET("/api/v1/account")),
    staleTime: Infinity,
  });
}

/** The actions of «Публикация в Telegram». Each answers with the link, which goes to the cache;
 *  a failure is the screen's to show (bot_unavailable and network_unavailable sit in the section). */
export function useAccountActions() {
  const client = useQueryClient();
  const onSuccess = (link: AccountLink) => client.setQueryData(keys.account, link);
  return {
    /** A one-time code in a link to the bot; Start pressed comes as accountChanged */
    start: useMutation({
      mutationFn: () => call(api.POST("/api/v1/account/link")),
      onSuccess,
    }),
    /** «Отмена» while waiting for Start, otherwise «Отвязать» */
    drop: useMutation({
      mutationFn: () => call(api.DELETE("/api/v1/account/link")),
      onSuccess,
    }),
    /** «Проверить связь» */
    check: useMutation({
      mutationFn: () => call(api.POST("/api/v1/account/check")),
      onSuccess,
    }),
  };
}

export function useProjects() {
  return useQuery({
    queryKey: keys.projects,
    queryFn: () => call(api.GET("/api/v1/projects")),
  });
}

/** The project open in the editor. Read once: from then on the editor owns the scene. */
export function useProject(id: string | undefined) {
  return useQuery({
    queryKey: keys.project(id ?? ""),
    queryFn: () => call(api.GET("/api/v1/projects/{id}", { params: { path: { id: id ?? "" } } })),
    enabled: id !== undefined,
    staleTime: Infinity,
  });
}

/** Running and recently finished jobs: after F5 they bring back their popups (artboard 8).
 *  jobUpdated adds to it; while the hub is down, the running ones in it are polled. */
export function useJobs() {
  return useQuery({
    queryKey: keys.jobs,
    queryFn: () => call(api.GET("/api/v1/jobs")),
    staleTime: Infinity,
  });
}

/** Downloads by link, newest first; downloadUpdated keeps them fresh (artboard 20). */
export function useDownloads() {
  return useQuery({
    queryKey: keys.downloads,
    queryFn: () => call(api.GET("/api/v1/downloads")),
    staleTime: Infinity,
  });
}

/** One background job; jobUpdated keeps it fresh. */
export function useJob(id: string | undefined) {
  return useQuery({
    queryKey: keys.job(id ?? ""),
    queryFn: () => call(api.GET("/api/v1/jobs/{jobId}", { params: { path: { jobId: id ?? "" } } })),
    enabled: id !== undefined,
    staleTime: Infinity,
  });
}

/** Size forecast of a segment that may not be saved yet (artboard 3a «Экспорт»). */
export function useEstimate(projectId: string | undefined, segment: Segment | undefined) {
  return useQuery({
    queryKey: ["estimate", projectId, segment],
    // An edit made since drops the forecast being measured: the server stops it too.
    queryFn: ({ signal }) =>
      call(
        api.POST("/api/v1/projects/{id}/estimate", {
          params: { path: { id: projectId ?? "" } },
          body: { segment: segment as Segment },
          signal,
        }),
      ),
    enabled: projectId !== undefined && segment !== undefined,
    staleTime: Infinity,
    // The previous forecast stays on screen while the next one is computed.
    placeholderData: (previous) => previous,
  });
}

/** «Мои паки»: what the app knows about its packs, without asking Telegram (artboard 7). */
export function usePacks() {
  return useQuery({
    queryKey: keys.packs,
    queryFn: () => call(api.GET("/api/v1/packs")),
  });
}

/** The open pack with its stickers; packChanged reads it again. */
export function usePack(name: string | undefined) {
  return useQuery({
    queryKey: keys.pack(name ?? ""),
    queryFn: () =>
      call(api.GET("/api/v1/packs/{pack}", { params: { path: { pack: name ?? "" } } })),
    enabled: name !== undefined,
  });
}

/** Everything the publish window asks about one project (artboard 7, six ways in). */
export function usePublishDraft(projectId: string | undefined) {
  return useQuery({
    queryKey: ["publish-draft", projectId],
    queryFn: () =>
      call(
        api.GET("/api/v1/projects/{id}/publish-draft", {
          params: { path: { id: projectId ?? "" } },
        }),
      ),
    enabled: projectId !== undefined,
  });
}

/** «Написать отзыв» (artboard 19·4): the server attaches its report to a bug and forwards the
 *  feedback to the cloud service. A failure is the window's to show, under its buttons. */
export function useSendFeedback() {
  return useMutation({
    mutationFn: (body: FeedbackRequest) => call(api.POST("/api/v1/feedback", { body })),
  });
}
