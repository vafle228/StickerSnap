// Downloads the pinned FFmpeg the server renders with into tools/ffmpeg/<rid>: ffmpeg, ffprobe
// and the license. An LGPL build with libvpx and no GPL parts (BtbN/FFmpeg-Builds, 9.0 for the
// animated WEBP decoder), checked against its SHA-256 before anything is unpacked. Month-end
// builds stay downloadable for about two years; to move to a newer one, change BUILD and the hashes,
// and COMMIT to the commit of that build. BtbN has no macOS builds: on a Mac of that architecture
// the same commit is built from source (tools/build-ffmpeg.mjs, about ten minutes).
// The binaries are never committed (.gitignore: ffmpeg/); packaging takes them from here (B20).
// Usage: node tools/fetch-ffmpeg.mjs [win-x64|linux-x64|win-arm64|linux-arm64|osx-arm64|osx-x64]
//   [--force]
import { createHash } from "node:crypto";
import { execFileSync } from "node:child_process";
import { cpSync, createWriteStream, existsSync, mkdirSync, mkdtempSync } from "node:fs";
import { readdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { Readable } from "node:stream";
import { pipeline } from "node:stream/promises";
import { fileURLToPath } from "node:url";
import { buildFfmpeg } from "./build-ffmpeg.mjs";

const BUILD = "autobuild-2026-09-30-13-08";
const VERSION = "n9.0.2-17-g2a571b6068";
/** The commit of release/9.0 VERSION names, which the macOS builds are built from */
const COMMIT = "2a571b606854520cf89804d8030c8b328e621689";
const ARCHIVES = {
  "win-x64": {
    file: `ffmpeg-${VERSION}-win64-lgpl-9.0.zip`,
    sha256: "6b264b9e6019103f601d98c292bd332fd87acf1c5e941ddff4fb71760fe63432",
  },
  "linux-x64": {
    file: `ffmpeg-${VERSION}-linux64-lgpl-9.0.tar.xz`,
    sha256: "2d41cbea0ca1a15029b638330740f78d6f5aeb062355bf425433fc938705350a",
  },
  "win-arm64": {
    file: `ffmpeg-${VERSION}-winarm64-lgpl-9.0.zip`,
    sha256: "d189d06c804b01e60e9677901f410f85900682f6a59f476b933a7fa48176b0c6",
  },
  "linux-arm64": {
    file: `ffmpeg-${VERSION}-linuxarm64-lgpl-9.0.tar.xz`,
    sha256: "b8cc09200780d37801179c803f597c22440b0b06c556addd2e6a07b27475ce37",
  },
};

/** Built from source on a Mac of that architecture; vpxTarget is libvpx's name of the system */
const SOURCE_BUILDS = {
  "osx-arm64": { arch: "arm64", vpxTarget: "arm64-darwin20-gcc" },
  "osx-x64": { arch: "x64", vpxTarget: "x86_64-darwin20-gcc" },
};

const args = process.argv.slice(2);
const force = args.includes("--force");
const host = { win32: "win", darwin: "osx" }[process.platform] ?? process.platform;
const rid = args.find((arg) => !arg.startsWith("--")) ?? `${host}-${process.arch}`;
const archive = ARCHIVES[rid];
const source = SOURCE_BUILDS[rid];
if (!archive && !source) {
  const known = [...Object.keys(ARCHIVES), ...Object.keys(SOURCE_BUILDS)];
  console.error(`No pinned FFmpeg for ${rid}; known: ${known.join(", ")}.`);
  process.exit(1);
}

const out = fileURLToPath(new URL(`ffmpeg/${rid}/`, import.meta.url));
const stamp = join(out, "VERSION");
const want = archive ? archive.file : `${VERSION} ${COMMIT} ${rid}`;
if (!force && existsSync(stamp) && readFileSync(stamp, "utf8").trim() === want) {
  console.log(`FFmpeg ${VERSION} for ${rid} is already in ${out}`);
  process.exit(0);
}

if (source) {
  if (process.platform !== "darwin" || process.arch !== source.arch) {
    console.error(`FFmpeg for ${rid} is built from source: run this on a Mac with ${source.arch}.`);
    process.exit(1);
  }
  const work = mkdtempSync(join(tmpdir(), "stickersnap-ffmpeg-"));
  try {
    rmSync(out, { recursive: true, force: true });
    buildFfmpeg({ commit: COMMIT, version: VERSION, vpxTarget: source.vpxTarget, work, out });
    writeFileSync(stamp, `${want}\n`);
    console.log(`FFmpeg ${VERSION} for ${rid} is in ${out}`);
  } finally {
    rmSync(work, { recursive: true, force: true });
  }
  process.exit(0);
}

const url = `https://github.com/BtbN/FFmpeg-Builds/releases/download/${BUILD}/${archive.file}`;
const work = mkdtempSync(join(tmpdir(), "stickersnap-ffmpeg-"));
try {
  console.log(`Downloading ${url}`);
  const response = await fetch(url);
  if (!response.ok || !response.body) throw new Error(`${url}: HTTP ${response.status}`);
  const downloaded = join(work, archive.file);
  const hash = createHash("sha256");
  const body = Readable.fromWeb(response.body);
  body.on("data", (chunk) => hash.update(chunk));
  await pipeline(body, createWriteStream(downloaded));

  const actual = hash.digest("hex");
  if (actual !== archive.sha256) {
    throw new Error(`${archive.file}: SHA-256 is ${actual}, expected ${archive.sha256}`);
  }

  // bsdtar unpacks .zip as well; Windows has it in System32, where Git's GNU tar does not shadow it
  const tar =
    process.platform === "win32"
      ? join(process.env.SystemRoot ?? "C:\\Windows", "System32", "tar.exe")
      : "tar";
  const unpacked = join(work, "unpacked");
  mkdirSync(unpacked);
  execFileSync(tar, ["-xf", downloaded, "-C", unpacked], { stdio: "inherit" });

  const [root] = readdirSync(unpacked);
  const exe = rid.startsWith("win") ? ".exe" : "";
  rmSync(out, { recursive: true, force: true });
  mkdirSync(out, { recursive: true });
  for (const tool of ["ffmpeg", "ffprobe"]) {
    cpSync(join(unpacked, root, "bin", tool + exe), join(out, tool + exe));
  }
  cpSync(join(unpacked, root, "LICENSE.txt"), join(out, "LICENSE.txt"));
  writeFileSync(stamp, `${want}\n`);
  console.log(`FFmpeg ${VERSION} for ${rid} is in ${out}`);
} finally {
  rmSync(work, { recursive: true, force: true });
}
