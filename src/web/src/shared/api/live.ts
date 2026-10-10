import type { QueryClient } from "@tanstack/react-query";
import { useQueryClient } from "@tanstack/react-query";
import { useEffect } from "react";
import { api, call } from "./client";
import { createHub, type EventHub, type HubState } from "./hub";
import { keys } from "./queries";
import type { Job, PackSummary, ProjectSummary } from "./types";

/* SignalR events land in the TanStack Query cache with setQueryData: every event is a whole
   snapshot, so a lost one breaks nothing (contract, «События SignalR»). After a reconnect the
   lists the hub could have missed are read again once; while the hub is down, running jobs
   are polled every second. */

const POLL_MS = 1000;

/** Puts `item` into a list: replaced in place, or added on top (lists are newest first). */
function upsert<T>(list: T[] | undefined, item: T, same: (a: T) => boolean): T[] | undefined {
  if (!list) return list;
  return list.some(same) ? list.map((x) => (same(x) ? item : x)) : [item, ...list];
}

export function applyJob(client: QueryClient, job: Job) {
  client.setQueryData(keys.job(job.id), job);
  client.setQueryData<Job[]>(keys.jobs, (list) => upsert(list, job, (j) => j.id === job.id));
}

const isRunning = (job: Job) => job.state === "queued" || job.state === "running";

/** Wires the hub into the cache; returns the function that unwires it. */
export function connectLive(client: QueryClient, hub: EventHub): () => void {
  let wasConnected = false;
  let poll: ReturnType<typeof setInterval> | undefined;

  const pollJobs = () => {
    const running = (client.getQueryData<Job[]>(keys.jobs) ?? []).filter(isRunning);
    for (const job of running) {
      call(api.GET("/api/v1/jobs/{jobId}", { params: { path: { jobId: job.id } } })).then(
        (fresh) => applyJob(client, fresh),
        () => {}, // the next tick tries again
      );
    }
  };

  const onState = (state: HubState) => {
    if (state === "connected") {
      clearInterval(poll);
      poll = undefined;
      if (wasConnected) {
        void client.invalidateQueries({ queryKey: keys.jobs });
        void client.invalidateQueries({ queryKey: keys.projects });
        void client.invalidateQueries({ queryKey: keys.account });
      }
      wasConnected = true;
    } else if (!poll) {
      poll = setInterval(pollJobs, POLL_MS);
    }
  };

  const off = [
    hub.onState(onState),
    hub.on("jobUpdated", (job) => applyJob(client, job)),
    hub.on("projectChanged", (project) => {
      // A save moves the project to the top of «Недавние».
      client.setQueryData<ProjectSummary[]>(
        keys.projects,
        (list) => list && [project, ...list.filter((p) => p.id !== project.id)],
      );
    }),
    hub.on("projectRemoved", ({ id }) => {
      client.setQueryData<ProjectSummary[]>(keys.projects, (list) =>
        list?.filter((p) => p.id !== id),
      );
      client.removeQueries({ queryKey: keys.project(id) });
    }),
    hub.on("accountChanged", (link) => client.setQueryData(keys.account, link)),
    hub.on("packChanged", (pack) => {
      client.setQueryData<PackSummary[]>(keys.packs, (list) =>
        upsert(list, pack, (p) => p.name === pack.name),
      );
      // The open pack is read again: the event carries only its summary.
      void client.invalidateQueries({ queryKey: keys.pack(pack.name) });
    }),
    hub.on("settingsChanged", (settings) => client.setQueryData(keys.settings, settings)),
  ];

  return () => {
    clearInterval(poll);
    for (const unsubscribe of off) unsubscribe();
  };
}

/** Mounted once at the root: one hub connection for the whole SPA. */
export function useLiveUpdates() {
  const client = useQueryClient();
  useEffect(() => {
    const hub = createHub();
    const disconnect = connectLive(client, hub);
    hub.start();
    return () => {
      disconnect();
      hub.stop();
    };
  }, [client]);
}
