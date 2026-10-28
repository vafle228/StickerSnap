// Builds FFmpeg from source for a system BtbN/FFmpeg-Builds has no build for (macOS): the same
// commit of release/9.0 the other builds come from, LGPL with a static libvpx and no GPL parts,
// nothing taken from the machine but zlib and the system's frameworks, so ffmpeg and ffprobe run
// on any Mac of that architecture. The sources are pinned by their git commits; git checks every
// object it fetches against its id. fetch-ffmpeg.mjs calls this, the cache keeps the result (CI).
// Needs git, make and the compiler (Xcode Command Line Tools), and nasm on x86-64.
import { execFileSync } from "node:child_process";
import { cpSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { cpus } from "node:os";
import { join } from "node:path";

/** libvpx v1.17.0 (BSD), the VP9 encoder of the stickers and the decoder of VP9 with alpha */
const LIBVPX = {
  url: "https://github.com/webmproject/libvpx",
  commit: "6df3ec34557879fff673706f4a1d9fbd0f3a6f0e",
};
const FFMPEG_URL = "https://github.com/FFmpeg/FFmpeg";

/** The oldest macOS the binaries run on: Electron's own minimum */
const MACOS_MIN = "12.0";

function checkout(url, commit, dir) {
  mkdirSync(dir, { recursive: true });
  const git = (...args) => execFileSync("git", args, { cwd: dir, stdio: "inherit" });
  git("init", "-q");
  git("fetch", "-q", "--depth", "1", url, commit);
  git("-c", "advice.detachedHead=false", "checkout", "-q", commit);
}

/**
 * Builds ffmpeg and ffprobe in `work` and copies them with the license into `out`.
 * @param {{ commit: string, version: string, vpxTarget: string, work: string, out: string }} build
 *   `version` is what `ffmpeg -version` says, the same string as the other systems' builds
 */
export function buildFfmpeg({ commit, version, vpxTarget, work, out }) {
  const jobs = String(cpus().length);
  const prefix = join(work, "prefix");
  const vpx = join(work, "libvpx");
  const ffmpeg = join(work, "ffmpeg");
  const mac = process.platform === "darwin";
  const env = {
    ...process.env,
    ...(mac ? { MACOSX_DEPLOYMENT_TARGET: MACOS_MIN } : {}),
    PKG_CONFIG_PATH: join(prefix, "lib", "pkgconfig"),
  };
  const run = (cwd, command, ...args) =>
    execFileSync(command, args, { cwd, env, stdio: "inherit" });

  console.log(`Building libvpx ${LIBVPX.commit} for ${vpxTarget}`);
  checkout(LIBVPX.url, LIBVPX.commit, vpx);
  run(
    vpx,
    "./configure",
    `--target=${vpxTarget}`,
    `--prefix=${prefix}`,
    "--enable-static",
    "--disable-shared",
    "--enable-pic",
    "--enable-vp9-highbitdepth",
    "--disable-examples",
    "--disable-tools",
    "--disable-docs",
    "--disable-unit-tests",
  );
  run(vpx, "make", `-j${jobs}`);
  run(vpx, "make", "install");

  console.log(`Building FFmpeg ${version}`);
  checkout(FFMPEG_URL, commit, ffmpeg);
  // A shallow checkout has no tags: the VERSION file gives ffmpeg the name of its commit
  writeFileSync(join(ffmpeg, "VERSION"), `${version}\n`);
  run(
    ffmpeg,
    "./configure",
    `--prefix=${prefix}`,
    "--pkg-config-flags=--static",
    ...(mac
      ? [
          `--extra-cflags=-mmacosx-version-min=${MACOS_MIN}`,
          `--extra-ldflags=-mmacosx-version-min=${MACOS_MIN}`,
        ]
      : []),
    "--enable-static",
    "--disable-shared",
    // Nothing found on the machine by chance (Homebrew's libraries): only what is asked for
    "--disable-autodetect",
    "--enable-zlib",
    "--enable-libvpx",
    "--disable-gpl",
    "--disable-nonfree",
    "--disable-ffplay",
    "--disable-doc",
    "--disable-network",
    "--disable-debug",
  );
  run(ffmpeg, "make", `-j${jobs}`);

  mkdirSync(out, { recursive: true });
  for (const tool of ["ffmpeg", "ffprobe"]) cpSync(join(ffmpeg, tool), join(out, tool));
  // FFmpeg is LGPL; libvpx is BSD, whose notice goes with the binaries it is linked into
  const license = [
    readFileSync(join(ffmpeg, "COPYING.LGPLv2.1"), "utf8"),
    "\n\nThese builds include libvpx, under this license:\n\n",
    readFileSync(join(vpx, "LICENSE"), "utf8"),
  ];
  writeFileSync(join(out, "LICENSE.txt"), license.join(""));
}
