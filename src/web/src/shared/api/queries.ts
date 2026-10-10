import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { notifyProblem } from "../notify/problems";
import { api, call } from "./client";
import type { Settings, SettingsPatch } from "./types";

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
