// Downloads the pinned yt-dlp the server imports videos by link with (YouTube, Giphy) into
// tools/yt-dlp/<rid>: the standalone executable, checked against its SHA-256 before it is kept,
// with its license (Unlicense) and the licenses of what the executable bundles (Python and
// others). Sites change and yt-dlp follows them, so move to a newer release when downloads
// break: change VERSION and the hashes (SHA2-256SUMS of the release).
// The binaries are never committed (.gitignore: yt-dlp/); packaging takes them from here.
// Usage: node tools/fetch-ytdlp.mjs [win-x64|linux-x64|win-arm64|linux-arm64] [--force]
import { createHash } from "node:crypto";
import { chmodSync, existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const VERSION = "2026.10.26";
const EXECUTABLES = {
  "win-x64": {
    file: "yt-dlp.exe",
    sha256: "f5389d3698ecde4ef06f4e6b1ce0294fe9ac1396228c8990b8d3416d6d177a7d",
  },
  "linux-x64": {
    file: "yt-dlp_linux",
    sha256: "767ba9dce7052aa196f8b12652f433d76de128ba168966fa84b6394d5e6c6795",
  },
  "win-arm64": {
    file: "yt-dlp_arm64.exe",
    sha256: "0066dbac3c124028e2dcae2a197fccee33cd91f456313d24f7af89fd54590e98",
  },
  "linux-arm64": {
    file: "yt-dlp_linux_aarch64",
    sha256: "e87e9533e89fe5889abaa21f5114dfe7756d6aa6219b83551add04d48b18e483",
  },
};

const args = process.argv.slice(2);
const force = args.includes("--force");
const rid =
  args.find((arg) => !arg.startsWith("--")) ??
  `${process.platform === "win32" ? "win" : process.platform}-${process.arch}`;
const executable = EXECUTABLES[rid];
if (!executable) {
  console.error(`No pinned yt-dlp for ${rid}; known: ${Object.keys(EXECUTABLES).join(", ")}.`);
  process.exit(1);
}

const out = fileURLToPath(new URL(`yt-dlp/${rid}/`, import.meta.url));
const stamp = join(out, "VERSION");
const want = `${VERSION} ${executable.file}`;
if (!force && existsSync(stamp) && readFileSync(stamp, "utf8").trim() === want) {
  console.log(`yt-dlp ${VERSION} for ${rid} is already in ${out}`);
  process.exit(0);
}

const RELEASE = `https://github.com/yt-dlp/yt-dlp/releases/download/${VERSION}`;
const SOURCE = `https://raw.githubusercontent.com/yt-dlp/yt-dlp/${VERSION}`;

async function download(url) {
  console.log(`Downloading ${url}`);
  const response = await fetch(url);
  if (!response.ok) throw new Error(`${url}: HTTP ${response.status}`);
  return Buffer.from(await response.arrayBuffer());
}

const binary = await download(`${RELEASE}/${executable.file}`);
const actual = createHash("sha256").update(binary).digest("hex");
if (actual !== executable.sha256) {
  throw new Error(`${executable.file}: SHA-256 is ${actual}, expected ${executable.sha256}`);
}
const license = await download(`${SOURCE}/LICENSE`);
const bundled = await download(`${SOURCE}/THIRD_PARTY_LICENSES.txt`);

// The server looks for yt-dlp(.exe) by that name, whatever the release calls the file
const name = rid.startsWith("win") ? "yt-dlp.exe" : "yt-dlp";
rmSync(out, { recursive: true, force: true });
mkdirSync(out, { recursive: true });
writeFileSync(join(out, name), binary);
if (!rid.startsWith("win")) chmodSync(join(out, name), 0o755);
writeFileSync(join(out, "LICENSE.txt"), license);
writeFileSync(join(out, "THIRD_PARTY_LICENSES.txt"), bundled);
writeFileSync(stamp, `${want}\n`);
console.log(`yt-dlp ${VERSION} for ${rid} is in ${out}`);
