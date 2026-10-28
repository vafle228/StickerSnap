import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

const read = (path: string) => readFileSync(new URL(path, import.meta.url), "utf8");

// electron-builder downloads Electron by the version written in package.json: npm workspaces
// hoist the package to the root node_modules, so a range like "^44.9.3" fails the installers.
describe("installer packaging", () => {
  it("pins Electron to an exact version", () => {
    const pkg = JSON.parse(read("../package.json")) as {
      devDependencies: Record<string, string>;
    };
    expect(pkg.devDependencies.electron).toMatch(/^\d+\.\d+\.\d+$/);
  });

  // The Mac keeps its own copy of the list (its folders are osx-<arch>, electron-builder's
  // ${os} there is mac): whatever Windows and Linux carry, a Mac carries too.
  it("puts the same server, FFmpeg, yt-dlp and licenses into the Mac app", () => {
    const sources = [...read("../electron-builder.yml").matchAll(/- from: (\S+)/g)].map(
      (m) => m[1] ?? "",
    );
    const shared = sources.filter((from) => from.includes("${os}-${arch}"));
    const mac = sources.filter((from) => from.includes("osx-${arch}"));
    expect(shared.length).toBeGreaterThan(0);
    expect(mac).toEqual(shared.map((from) => from.replace("${os}-${arch}", "osx-${arch}")));
  });
});
