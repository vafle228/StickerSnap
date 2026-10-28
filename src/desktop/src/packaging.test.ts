import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

// electron-builder downloads Electron by the version written in package.json: npm workspaces
// hoist the package to the root node_modules, so a range like "^44.9.3" fails the installers.
describe("installer packaging", () => {
  it("pins Electron to an exact version", () => {
    const pkg = JSON.parse(readFileSync(new URL("../package.json", import.meta.url), "utf8")) as {
      devDependencies: Record<string, string>;
    };
    expect(pkg.devDependencies.electron).toMatch(/^\d+\.\d+\.\d+$/);
  });
});
