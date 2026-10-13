// The web server of the e2e on the real server (playwright.server.config.ts): a clean data
// directory, a test clip made by the pinned FFmpeg, then the ASP.NET server in Production with
// the production SPA from its wwwroot (npm run build), so no demo data can be in the way.
// Usage: node e2e-server/start.mjs <work dir> <ffmpeg dir> <port> [--no-build]
import { execFileSync, spawn } from "node:child_process";
import { existsSync, mkdirSync, rmSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const [work, ffmpegDir, port] = process.argv.slice(2);
const noBuild = process.argv.includes("--no-build");
const exe = process.platform === "win32" ? ".exe" : "";
const ffmpeg = join(ffmpegDir, `ffmpeg${exe}`);
const api = fileURLToPath(new URL("../../server/StickerSnap.Api", import.meta.url));

if (!existsSync(ffmpeg)) {
  console.error(`No FFmpeg in ${ffmpegDir}: run \`npm run fetch:ffmpeg\` first.`);
  process.exit(1);
}
if (!existsSync(join(api, "wwwroot", "index.html"))) {
  console.error("No SPA in the server's wwwroot: run `npm run build` first.");
  process.exit(1);
}

rmSync(work, { recursive: true, force: true });
mkdirSync(join(work, "data"), { recursive: true });

// 4 s of a moving test picture at 30 fps. MPEG-4 Part 2, not H.264: the LGPL build has no
// x264, and the server then makes its own playable copy for <video> (B11), as for a user's MOV.
execFileSync(ffmpeg, [
  ...["-hide_banner", "-loglevel", "error", "-f", "lavfi"],
  ...["-i", "testsrc2=size=640x360:rate=30", "-t", "4"],
  ...["-pix_fmt", "yuv420p", "-c:v", "mpeg4", "-q:v", "4", join(work, "clip.mp4")],
]);

const server = spawn(
  "dotnet",
  [
    ...["run", "--project", api, "-c", "Release", "--no-launch-profile"],
    ...(noBuild ? ["--no-build"] : []),
  ],
  {
    stdio: "inherit",
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: "Production",
      ASPNETCORE_URLS: `http://127.0.0.1:${port}`,
      StickerSnap__DataDir: join(work, "data"),
      StickerSnap__FfmpegDir: ffmpegDir,
    },
  },
);
for (const signal of ["SIGINT", "SIGTERM"]) process.on(signal, () => server.kill(signal));
server.on("exit", (code) => process.exit(code ?? 1));
