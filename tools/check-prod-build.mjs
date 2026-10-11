// The production build (npm run build → src/server/wwwroot) carries no dev tools: no MSW and
// its service worker, no demo scenarios, no dev bar. CI runs this after the build, so the demo
// data never reaches an installer or the Docker image.
// Usage: node tools/check-prod-build.mjs
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("../src/server/wwwroot/", import.meta.url));

// Words only the dev tools have: MSW's logs, the dev bar's label and link (src/web/src/dev).
const MARKERS = ["mockServiceWorker", "[MSW]", "Режим разработки", "Витрина ДС"];

if (!existsSync(join(root, "index.html"))) {
  console.error("No build in src/server/wwwroot: run `npm run build` first.");
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
