import { spawn } from "node:child_process";
import { randomBytes } from "node:crypto";
import { createServer } from "node:net";
import { join } from "node:path";
import type { Writable } from "node:stream";

/** How to start the local ASP.NET server. */
export interface ServerCommand {
  command: string;
  args: string[];
  cwd: string;
  /** What the server is told besides the shell's own variables */
  env?: Record<string, string>;
}

export interface Layout {
  /** app.isPackaged: an installed app carries the published server in its resources */
  packaged: boolean;
  /** process.resourcesPath */
  resourcesPath: string;
  /** app.getAppPath(): src/desktop when run from the repository */
  appPath: string;
  platform: NodeJS.Platform;
  arch: string;
  exists: (path: string) => boolean;
}

/** The .NET runtime id the server is published for: win-x64, linux-x64 */
export const runtimeId = (platform: NodeJS.Platform, arch: string) =>
  `${platform === "win32" ? "win" : platform}-${arch}`;

/** The installed app runs its published server from resources/server, with FFmpeg next to it.
 *  From the repository it takes the one `npm run server` published into server-dist, or builds
 *  and runs the source, and points it at the FFmpeg `npm run fetch:ffmpeg` put in tools/ffmpeg. */
export function serverCommand(layout: Layout): ServerCommand {
  const windows = layout.platform === "win32";
  const exe = windows ? "StickerSnap.Api.exe" : "StickerSnap.Api";
  const rid = runtimeId(layout.platform, layout.arch);
  if (layout.packaged) {
    const dir = join(layout.resourcesPath, "server");
    return { command: join(dir, exe), args: [], cwd: dir };
  }

  const ffmpeg = join(layout.appPath, "..", "..", "tools", "ffmpeg", rid);
  const env = layout.exists(join(ffmpeg, windows ? "ffmpeg.exe" : "ffmpeg"))
    ? { env: { StickerSnap__FfmpegDir: ffmpeg } }
    : {};
  const dir = join(layout.appPath, "server-dist", rid);
  if (layout.exists(join(dir, exe))) {
    return { command: join(dir, exe), args: [], cwd: dir, ...env };
  }
  const project = join(layout.appPath, "..", "server", "StickerSnap.Api");
  return {
    command: "dotnet",
    args: ["run", "--project", project, "--no-launch-profile"],
    cwd: project,
    ...env,
  };
}

/** A token nobody can guess, new on every launch (contract, «Доступ») */
export const newToken = () => randomBytes(32).toString("base64url");

function listen(port: number): Promise<number | null> {
  return new Promise((resolve) => {
    const probe = createServer();
    probe.once("error", () => resolve(null));
    probe.listen(port, "127.0.0.1", () => {
      const address = probe.address();
      const bound = typeof address === "object" && address ? address.port : null;
      probe.close(() => resolve(bound));
    });
  });
}

/** The port of the last launch while it is free, so the SPA keeps its origin and with it its
 *  localStorage; otherwise any free one. */
export async function choosePort(preferred?: number): Promise<number> {
  if (preferred && (await listen(preferred)) === preferred) return preferred;
  const port = await listen(0);
  if (!port) throw new Error("No free port on 127.0.0.1");
  return port;
}

export class ServerStartError extends Error {
  /** The last lines the server printed, for the error dialog and the log */
  readonly output: string;

  constructor(message: string, output: string) {
    super(message);
    this.name = "ServerStartError";
    this.output = output;
  }
}

export interface RunningServer {
  baseUrl: string;
  token: string;
  /** Resolves with the exit code when the server stops, whoever stopped it */
  exited: Promise<number | null>;
  stop(): Promise<void>;
}

export interface StartOptions {
  port: number;
  token: string;
  /** Where the server's output goes (userData/logs/server.log) */
  log?: Writable;
  /** How long the server may take to answer; `dotnet run` builds first */
  timeoutMs: number;
}

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/** Starts the server on 127.0.0.1:`port` and resolves once it answers HTTP. The server learns
 *  the token, that it runs as the desktop app, and the shell's pid: it quits by itself when the
 *  shell is gone, even if the shell crashed (Program.cs). */
export async function startServer(
  cmd: ServerCommand,
  options: StartOptions,
): Promise<RunningServer> {
  const baseUrl = `http://127.0.0.1:${options.port}`;
  const child = spawn(cmd.command, cmd.args, {
    cwd: cmd.cwd,
    env: {
      ...process.env,
      ...cmd.env,
      ASPNETCORE_URLS: baseUrl,
      StickerSnap__Token: options.token,
      StickerSnap__Mode: "desktop",
      StickerSnap__ParentPid: String(process.pid),
      DOTNET_NOLOGO: "1",
    },
    stdio: ["ignore", "pipe", "pipe"],
    windowsHide: true,
  });

  let tail = "";
  const keep = (chunk: Buffer) => {
    tail = (tail + chunk.toString()).slice(-4000);
    options.log?.write(chunk);
  };
  child.stdout.on("data", keep);
  child.stderr.on("data", keep);

  let done = false;
  const exited = new Promise<number | null>((resolve) => {
    child.once("error", (error) => {
      keep(Buffer.from(`${error.message}\n`));
      done = true;
      resolve(null);
    });
    child.once("exit", (code) => {
      done = true;
      resolve(code);
    });
  });

  const stop = async () => {
    if (done) return;
    child.kill();
    const gone = await Promise.race([exited.then(() => true), sleep(5000).then(() => false)]);
    if (!gone) child.kill("SIGKILL");
    await exited;
  };

  const deadline = Date.now() + options.timeoutMs;
  while (!done) {
    try {
      await fetch(`${baseUrl}/`, { signal: AbortSignal.timeout(1000) });
      return { baseUrl, token: options.token, exited, stop };
    } catch {
      if (Date.now() > deadline) {
        await stop();
        throw new ServerStartError(`The server did not answer in ${options.timeoutMs} ms`, tail);
      }
      await sleep(200);
    }
  }
  const code = await exited;
  throw new ServerStartError(`The server exited with code ${String(code)}`, tail);
}
