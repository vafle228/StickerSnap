import { setupServer } from "msw/node";
import { afterAll, afterEach, describe, expect, it, vi } from "vitest";
import { createDb, type MockDb } from "./data";
import { handlers } from "./handlers";
import type { Scenario } from "./scenarios";

/* The demo server answers through the SPA's own typed client, the way screens call it:
   what «?mock» shows has to behave like the contract says the server does. */

// The handlers' paths are relative, as in the browser: they resolve against the page's address.
vi.stubGlobal("location", new URL("http://127.0.0.1:5180/"));
const server = setupServer();
// openapi-fetch keeps the fetch it finds when the client is made, so the client is loaded
// only after MSW has put its own fetch in place.
server.listen({ onUnhandledRequest: "error" });
const { api, ApiError, call } = await import("../shared/api/client");
let db: MockDb;

function use(scenario: Scenario) {
  db = createDb(scenario);
  server.resetHandlers(...handlers(db));
}

/** The code of the Problem a request fails with. */
async function failure(request: Promise<unknown>): Promise<string | undefined> {
  const error: unknown = await request.then(
    () => undefined,
    (e: unknown) => e,
  );
  return error instanceof ApiError ? error.problem?.code : undefined;
}

const FIRST = "6f1d2c3a-1b2c-4d5e-8f90-0a1b2c3d4e51";
const DAMAGED = "6f1d2c3a-1b2c-4d5e-8f90-0a1b2c3d4e52";
const ids = async () => (await call(api.GET("/api/v1/projects"))).map((p) => p.id);
const byId = (id: string) => ({ params: { path: { id } } });

describe("demo server", () => {
  afterEach(() => server.resetHandlers());
  afterAll(() => {
    server.close();
    vi.unstubAllGlobals();
  });

  describe("app", () => {
    const mode = async () => (await call(api.GET("/api/v1/app"))).mode;

    it("runs as the web version, and as the desktop app in «Десктоп»", async () => {
      use("ok");
      expect(await mode()).toBe("web");
      use("desktop");
      expect(await mode()).toBe("desktop");
    });

    it("runs as the desktop app in every scenario inside the desktop shell", async () => {
      vi.stubGlobal("stickersnap", { server: {} });
      try {
        use("ok");
        expect(await mode()).toBe("desktop");
      } finally {
        vi.stubGlobal("stickersnap", undefined);
      }
    });
  });

  describe("projects", () => {
    it("lists the recent projects newest first", async () => {
      use("ok");
      const list = await call(api.GET("/api/v1/projects"));
      const dates = list.map((p) => p.updatedAt);
      expect(list).toHaveLength(7);
      expect(dates).toEqual([...dates].sort().reverse());
    });

    it("has nothing to list on the first launch", async () => {
      use("no-projects");
      expect(await ids()).toEqual([]);
    });

    it("makes a new project with one segment at the top of the list", async () => {
      use("ok");
      // An upload refers to a file the browser sent first
      db.uploads.set("u-1", new File(["video"], "Кот прыгает.mp4", { type: "video/mp4" }));
      const source = { kind: "upload" as const, uploadId: "u-1" };
      const project = await call(api.POST("/api/v1/projects", { body: { source } }));

      expect(project.scene.segments).toHaveLength(1);
      expect((await ids())[0]).toBe(project.id);
    });

    it("renames a project", async () => {
      use("ok");
      const summary = await call(
        api.PATCH("/api/v1/projects/{id}", { ...byId(FIRST), body: { name: "Кот" } }),
      );
      expect(summary.name).toBe("Кот");
    });

    it("fails a rename with save_failed in «Ошибки проекта»", async () => {
      use("project-errors");
      const rename = call(
        api.PATCH("/api/v1/projects/{id}", { ...byId(FIRST), body: { name: "Кот" } }),
      );
      expect(await failure(rename)).toBe("save_failed");
    });

    it("cannot open the damaged project in «Ошибки проекта»", async () => {
      use("project-errors");
      expect(await failure(call(api.GET("/api/v1/projects/{id}", byId(DAMAGED))))).toBe(
        "project_invalid",
      );
    });

    it("brings a forgotten project back by its path, the way «Вернуть» does", async () => {
      use("ok");
      const path = db.projects[0]?.path ?? "";
      await call(
        api.DELETE("/api/v1/projects/{id}", {
          params: { path: { id: FIRST }, query: { mode: "forget" } },
        }),
      );
      expect(await ids()).not.toContain(FIRST);

      await call(api.POST("/api/v1/projects/open", { body: { path } }));
      expect(await ids()).toContain(FIRST);
    });

    it("cannot bring back a project moved to the trash", async () => {
      use("ok");
      const path = db.projects[0]?.path ?? "";
      await call(
        api.DELETE("/api/v1/projects/{id}", {
          params: { path: { id: FIRST }, query: { mode: "trash" } },
        }),
      );
      const open = call(api.POST("/api/v1/projects/open", { body: { path } }));
      expect(await failure(open)).toBe("project_not_found");
    });
  });

  describe("autosave", () => {
    it("takes the scene at the current revision and moves the revision on", async () => {
      use("ok");
      const project = await call(api.GET("/api/v1/projects/{id}", byId(FIRST)));
      const saved = await call(
        api.PUT("/api/v1/projects/{id}/scene", {
          ...byId(FIRST),
          body: { revision: project.revision, scene: project.scene },
        }),
      );
      expect(saved.revision).toBe(project.revision + 1);
    });

    it("refuses a scene saved over an older revision", async () => {
      use("ok");
      const project = await call(api.GET("/api/v1/projects/{id}", byId(FIRST)));
      const stale = call(
        api.PUT("/api/v1/projects/{id}/scene", {
          ...byId(FIRST),
          body: { revision: project.revision - 1, scene: project.scene },
        }),
      );
      expect(await failure(stale)).toBe("revision_conflict");
    });

    it("«Не сохраняется»: fails the first save with save_failed and takes the next one", async () => {
      use("save-fails");
      const project = await call(api.GET("/api/v1/projects/{id}", byId(FIRST)));
      const save = () =>
        call(
          api.PUT("/api/v1/projects/{id}/scene", {
            ...byId(FIRST),
            body: { revision: project.revision, scene: project.scene },
          }),
        );
      expect(await failure(save())).toBe("save_failed");
      expect((await save()).revision).toBe(project.revision + 1);
    });
  });

  describe("source", () => {
    it("names the segments past the end of a shorter video picked instead", async () => {
      use("source-missing");
      const { project, segmentsOutOfRange } = await call(
        api.PUT("/api/v1/projects/{id}/source", {
          ...byId(FIRST),
          body: { source: { kind: "upload", uploadId: "u-2" } },
        }),
      );
      expect(project.source.status).toBe("ok");
      expect(segmentsOutOfRange).toEqual(["seg-2"]);
    });

    it("asks for frames only inside the source", async () => {
      use("ok");
      const frames = call(
        api.GET("/api/v1/projects/{id}/frames", {
          params: { path: { id: FIRST }, query: { from: 230, count: 20, height: 48 } },
          parseAs: "blob",
        }),
      );
      expect(await failure(frames)).toBe("bad_request");
    });
  });

  describe("packs and publishing", () => {
    const publish = (pack: string) =>
      call(
        api.POST("/api/v1/jobs", {
          body: {
            kind: "publish",
            projectId: FIRST,
            revision: 3,
            items: [{ segmentId: "seg-1", emoji: ["😺"] }],
            target: { kind: "existing", pack },
          },
        }),
      );

    it("does not publish into a full pack", async () => {
      use("pack-full");
      expect(await failure(publish("kot_divan_by_StickerSnapAppBot"))).toBe("pack_full");
    });

    it("does not publish without the network", async () => {
      use("offline");
      expect(await failure(publish("kot_divan_by_StickerSnapAppBot"))).toBe("network_unavailable");
    });

    it("suggests another name when the pack's name is taken", async () => {
      use("ok");
      const check = await call(
        api.POST("/api/v1/packs/name-check", {
          body: { title: "Кот на диване", name: "kot_divan" },
        }),
      );
      expect(check).toMatchObject({ problems: ["taken"], suggestion: "kot_divan_2" });
    });

    it("imports only packs of our bot", async () => {
      use("ok");
      const link = "https://t.me/addstickers/someone_elses_pack";
      expect(await failure(call(api.POST("/api/v1/packs/import", { body: { link } })))).toBe(
        "not_our_pack",
      );
    });
  });
});
