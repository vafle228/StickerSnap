// A production build carries no dev tools: no MSW and its service worker, no demo scenarios, no dev
// bar. CI runs this after each build, so the demo data never reaches an installer, the Docker image or
// the Mini App in Telegram.
// Usage: node tools/check-prod-build.mjs [dir]
//   dir: the build to check, src/server/StickerSnap.Api/wwwroot (the SPA) by default;
//        src/telegram/miniapp/dist for the Mini App (npm run build:miniapp)
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";

const repo = fileURLToPath(new URL("../", import.meta.url));
const dir = process.argv[2] ?? "src/server/StickerSnap.Api/wwwroot";
const root = resolve(repo, dir) + sep;

// Words only the dev tools have: MSW's logs, the dev bars' label and the showcase link
// (src/web/src/dev, src/telegram/miniapp/src/dev).
const MARKERS = ["mockServiceWorker", "[MSW]", "Режим разработки", "Витрина ДС"];

if (!existsSync(join(root, "index.html"))) {
  console.error(`No build in ${dir}: build it first.`);
  process.exit(1);
}

const files = readdirSync(root, { recursive: true, withFileTypes: true })
  .filter((entry) => entry.isFile())
  .map((entry) => join(entry.parentPath, entry.name));

const problems = [];
for (const file of files) {
  const name = file.slice(root.length).replaceAll("\\", "/");
  if (name === "mockServiceWorker.js") problems.push(`${name}: the MSW service worker`);
  if (!/\.(js|html|css)$/.test(name)) continue;
  const text = readFileSync(file, "utf8");
  for (const marker of MARKERS) {
    if (text.includes(marker)) problems.push(`${name}: «${marker}»`);
  }
}

if (problems.length > 0) {
  console.error("The production build carries dev tools (is it `npm run build:dev`?):");
  for (const problem of problems) console.error(`  ${problem}`);
  process.exit(1);
}
console.log(`The production build is clean: ${files.length} files, no demo data or dev bar.`);
