import { QueryClient } from "@tanstack/react-query";
import { describe, expect, it } from "vitest";
import { Listeners, type EventHub } from "./hub";
import { connectLive } from "./live";
import { keys } from "./queries";
import type { Job, ProjectSummary } from "./types";

function fakeHub() {
  const listeners = new Listeners();
  const hub: EventHub = {
    on: (event, handler) => listeners.on(event, handler),
    onState: (handler) => listeners.onState(handler),
    start: () => {},
    stop: () => {},
  };
  return { hub, listeners };
}

const job = (id: string, fraction: number): Job => ({
  id,
  kind: "export",
  projectId: "00000000-0000-4000-8000-000000000001",
  state: fraction < 1 ? "running" : "succeeded",
  createdAt: "2026-10-10T10:00:00Z",
  progress: { fraction, stage: "pass2" },
  items: [],
});

const project = (id: string, name: string): ProjectSummary => ({
  id,
  name,
  updatedAt: "2026-10-10T10:00:00Z",
  segmentCount: 1,
  preset: "sticker",
  durationSec: 3,
  coverUrl: `/api/v1/projects/${id}/cover`,
  sourceStatus: "ok",
});

describe("live updates", () => {
  it("puts a job snapshot into its own query and into the list", () => {
    const client = new QueryClient();
    const { hub, listeners } = fakeHub();
    client.setQueryData<Job[]>(keys.jobs, [job("a", 0.2)]);
    const stop = connectLive(client, hub);

    listeners.emit("jobUpdated", job("a", 0.6));
    listeners.emit("jobUpdated", job("b", 0.1));

    expect(client.getQueryData<Job>(keys.job("a"))?.progress.fraction).toBe(0.6);
    expect(client.getQueryData<Job[]>(keys.jobs)?.map((j) => j.id)).toEqual(["b", "a"]);
    stop();
  });

  it("moves a saved project to the top and drops a removed one", () => {
    const client = new QueryClient();
    const { hub, listeners } = fakeHub();
    client.setQueryData(keys.projects, [project("1", "Кот"), project("2", "Пёс")]);
    client.setQueryData(keys.project("1"), { id: "1" });
    const stop = connectLive(client, hub);

    listeners.emit("projectChanged", project("2", "Пёс 2"));
    expect(client.getQueryData<ProjectSummary[]>(keys.projects)?.map((p) => p.name)).toEqual([
      "Пёс 2",
      "Кот",
    ]);

    listeners.emit("projectRemoved", { id: "1" });
    expect(client.getQueryData<ProjectSummary[]>(keys.projects)).toHaveLength(1);
    expect(client.getQueryData(keys.project("1"))).toBeUndefined();
    stop();
  });

  it("reads jobs, projects and the account again after a reconnect, not on the first connect", () => {
    const client = new QueryClient();
    const { hub, listeners } = fakeHub();
    client.setQueryData(keys.jobs, []);
    client.setQueryData(keys.settings, {});
    const stop = connectLive(client, hub);
    const invalid = (key: readonly unknown[]) => client.getQueryState(key)?.isInvalidated;

    listeners.setState("connected");
    expect(invalid(keys.jobs)).toBe(false);

    listeners.setState("reconnecting");
    listeners.setState("connected");
    expect(invalid(keys.jobs)).toBe(true);
    expect(invalid(keys.settings)).toBe(false);
    stop();
  });
});
