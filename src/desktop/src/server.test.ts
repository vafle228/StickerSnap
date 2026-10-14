import { createServer } from "node:net";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import {
  choosePort,
  newToken,
  serverCommand,
  ServerStartError,
  startServer,
  type Layout,
  type ServerCommand,
} from "./server.js";

const layout = (over: Partial<Layout>): Layout => ({
  packaged: false,
  resourcesPath: "/opt/StickerSnap/resources",
  appPath: "/repo/src/desktop",
  platform: "linux",
  arch: "x64",
  exists: () => false,
  ...over,
});

/** A stand-in for the ASP.NET server: node with a script, listening where it is told. */
const fake = (script: string): ServerCommand => ({
  command: process.execPath,
  args: ["-e", script],
  cwd: process.cwd(),
});

const ECHO_ENV = `
  const http = require("node:http");
  const url = new URL(process.env.ASPNETCORE_URLS);
  http.createServer((req, res) => {
    res.setHeader("Content-Type", "application/json");
    res.end(JSON.stringify({
      token: process.env.StickerSnap__Token,
      mode: process.env.StickerSnap__Mode,
      parent: process.env.StickerSnap__ParentPid,
    }));
  }).listen(Number(url.port), url.hostname);
`;

describe("where the server comes from", () => {
  it("runs the published server from the installed app's resources", () => {
    const cmd = serverCommand(layout({ packaged: true, platform: "win32", resourcesPath: "R" }));
    const dir = join("R", "server");
    expect(cmd).toEqual({ command: join(dir, "StickerSnap.Api.exe"), args: [], cwd: dir });
  });

  it("runs the server published into server-dist from the repository", () => {
    const dir = join("/repo/src/desktop", "server-dist", "linux-x64");
    const cmd = serverCommand(layout({ exists: (p) => p === join(dir, "StickerSnap.Api") }));
    expect(cmd).toEqual({ command: join(dir, "StickerSnap.Api"), args: [], cwd: dir });
  });

  it("builds and runs the source when nothing is published", () => {
    const cmd = serverCommand(layout({}));
    const project = join("/repo/src", "server", "StickerSnap.Api");
    expect(cmd).toEqual({
      command: "dotnet",
      args: ["run", "--project", project, "--no-launch-profile"],
      cwd: project,
    });
  });

  it("points the server run from the repository at the FFmpeg in tools/ffmpeg", () => {
    const dir = join("/repo/src/desktop", "server-dist", "linux-x64");
    const ffmpeg = join("/repo", "tools", "ffmpeg", "linux-x64");
    const there = [join(dir, "StickerSnap.Api"), join(ffmpeg, "ffmpeg")];
    const cmd = serverCommand(layout({ exists: (p) => there.includes(p) }));
    expect(cmd.env).toEqual({ StickerSnap__FfmpegDir: ffmpeg });
  });

  it("leaves the installed app to find the FFmpeg next to its server", () => {
    const cmd = serverCommand(layout({ packaged: true, exists: () => true }));
    expect(cmd.env).toBeUndefined();
  });
});

describe("the port", () => {
  it("is the last launch's while it is free, so the SPA keeps its origin", async () => {
    const free = await choosePort();
    expect(await choosePort(free)).toBe(free);
  });

  it("is another one when the last launch's is taken", async () => {
    const taken = createServer();
    await new Promise<void>((resolve) => taken.listen(0, "127.0.0.1", resolve));
    const address = taken.address();
    if (!address || typeof address === "string") throw new Error("no port");
    try {
      expect(await choosePort(address.port)).not.toBe(address.port);
    } finally {
      taken.close();
    }
  });
});

describe("starting the server", () => {
  it("makes a new unguessable token on every launch", () => {
    const token = newToken();
    expect(token).toMatch(/^[\w-]{43}$/);
    expect(newToken()).not.toBe(token);
  });

  it("hands it the token, the desktop mode and the shell's pid, and is ready once it answers", async () => {
    const port = await choosePort();
    const server = await startServer(fake(ECHO_ENV), { port, token: "t0k3n", timeoutMs: 10_000 });
    try {
      expect(server.baseUrl).toBe(`http://127.0.0.1:${port}`);
      const env = (await (await fetch(server.baseUrl)).json()) as Record<string, string>;
      expect(env).toEqual({ token: "t0k3n", mode: "desktop", parent: String(process.pid) });
    } finally {
      await server.stop();
    }
    await expect(fetch(server.baseUrl)).rejects.toThrow();
  });

  it("reports what the server printed when it exits before answering", async () => {
    const port = await choosePort();
    const start = startServer(fake(`console.error("port in use"); process.exit(3)`), {
      port,
      token: "t",
      timeoutMs: 10_000,
    });
    await expect(start).rejects.toBeInstanceOf(ServerStartError);
    await expect(start).rejects.toMatchObject({
      message: expect.stringContaining("code 3"),
      output: expect.stringContaining("port in use"),
    });
  });

  it("gives up on a server that never answers and stops it", async () => {
    const port = await choosePort();
    const start = startServer(fake("setInterval(() => {}, 1000)"), {
      port,
      token: "t",
      timeoutMs: 500,
    });
    await expect(start).rejects.toThrow(/did not answer/);
  });
});
