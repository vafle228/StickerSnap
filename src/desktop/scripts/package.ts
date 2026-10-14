/* The desktop app in one command, as the VS Code tasks run it:
     npm run package -w src/desktop -- [--dev] [--installer]
   Production by default: the SPA's production build, checked for dev tools, as «StickerSnap».
   --dev: the SPA's dev build with the demo scenarios and the dev bar, as «StickerSnap Dev» with
   its own id, profile and file names, so it never mixes with the real app. --installer: the NSIS
   installer (Windows) or AppImage and deb (Linux); without it the unpacked app, quicker.
   CI builds only production (workflow «Desktop»). */
import { execFileSync, execSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const dev = process.argv.includes("--dev");
const installer = process.argv.includes("--installer");

const repo = fileURLToPath(new URL("../../..", import.meta.url));
const desktop = fileURLToPath(new URL("..", import.meta.url));

const run = (command: string, cwd = repo) => execSync(command, { cwd, stdio: "inherit" });

/** electron-builder 26 needs Node 22.12+; on an older Node it runs on a Node 22 from npx. */
function node22(): string {
  const [major = 0, minor = 0] = process.versions.node.split(".").map(Number);
  if (major > 22 || (major === 22 && minor >= 12)) return process.execPath;
  console.log(
    `Node ${process.versions.node} is too old for electron-builder, taking Node 22 from npx`,
  );
  return execSync('npx -y node@22 -p "process.execPath"', { encoding: "utf8" }).trim();
}

const DEV_CONFIG = [
  "-c.appId=app.stickersnap.desktop.dev",
  "-c.productName=StickerSnap Dev",
  "-c.extraMetadata.productName=StickerSnap Dev",
  "-c.directories.output=release/dev",
  "-c.nsis.shortcutName=StickerSnap Dev",
  "-c.nsis.artifactName=StickerSnap-Dev-Setup-${version}.${ext}",
  "-c.linux.executableName=stickersnap-dev",
  "-c.linux.artifactName=StickerSnap-Dev-${version}-${arch}.${ext}",
  "-c.deb.packageName=stickersnap-dev",
];

run(dev ? "npm run build:dev" : "npm run build");
if (!dev) run("npm run check:prod-build");
run("npm run server", desktop);
run("npm run fetch:ffmpeg");
run("npm run build", desktop);
execFileSync(
  node22(),
  [
    fileURLToPath(new URL("../../../node_modules/electron-builder/cli.js", import.meta.url)),
    ...(installer ? [] : ["--dir"]),
    ...(dev ? DEV_CONFIG : []),
  ],
  { cwd: desktop, stdio: "inherit" },
);
console.log(
  `\n${dev ? "Dev" : "Production"} build is in src/desktop/${dev ? "release/dev" : "release"}`,
);
