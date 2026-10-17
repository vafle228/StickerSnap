import { defineConfig } from "vitest/config";

// Logic runs in node; component tests (*.test.tsx) opt into a DOM with
// `// @vitest-environment happy-dom` at the top of the file, as in src/web.
export default defineConfig({
  test: { include: ["src/**/*.test.{ts,tsx}"] },
});
