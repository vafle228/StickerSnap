/* Publishes the ASP.NET server, self-contained, for the shell to carry: npm run server
   [-- linux-x64]. electron-builder takes it from server-dist/<os>-<arch> (electron-builder.yml),
   and `electron .` from the repository runs it from there too. */
import { execFileSync } from "node:child_process";
import { existsSync, rmSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { runtimeId } from "../src/server.js";

const rid = process.argv[2] ?? runtimeId(process.platform, process.arch);
const server = fileURLToPath(new URL("../../server/StickerSnap.Server.csproj", import.meta.url));
const wwwroot = fileURLToPath(new URL("../../server/wwwroot/index.html", import.meta.url));
const out = fileURLToPath(new URL(`../server-dist/${rid}`, import.meta.url));

if (!existsSync(wwwroot)) {
  console.error("The SPA is not built: run `npm run build` in the repository root first.");
  process.exit(1);
}

rmSync(out, { recursive: true, force: true });
execFileSync(
  "dotnet",
  [
    "publish",
    server,
    "--configuration",
    "Release",
    "--runtime",
    rid,
    "--self-contained",
    "--output",
    out,
    // One executable next to wwwroot; the users have no .NET of their own.
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=none",
  ],
  { stdio: "inherit" },
);
console.log(`Published the server for ${rid} into ${out}`);
