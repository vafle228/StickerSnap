import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

/* Where the server of the e2e on the real server keeps its data and finds FFmpeg. The paths are
   fixed, not made per run: Playwright reads its config again in every worker, and the specs and
   the start script must agree on them. */

/** The folder of this run: start.ts empties it before the server starts. */
export const work = join(tmpdir(), "stickersnap-e2e-server");

/** The server's data directory: projects, uploads, renders. */
export const dataDir = join(work, "data");

/** A 4-second MP4 start.ts makes with FFmpeg, so no video is kept in git. */
export const clip = join(work, "clip.mp4");

/** The pinned FFmpeg of `npm run fetch:ffmpeg`, unless StickerSnap__FfmpegDir says otherwise. */
export function ffmpegDir(): string {
  const os = process.platform === "win32" ? "win" : process.platform;
  return (
    process.env.StickerSnap__FfmpegDir ??
    fileURLToPath(new URL(`../../../tools/ffmpeg/${os}-${process.arch}`, import.meta.url))
  );
}
