import { defineConfig } from "vitest/config";

// Unit tests of the shell's logic in node: no Electron, no window (e2e/ starts the real app).
export default defineConfig({
  test: {
    include: ["src/**/*.test.ts"],
  },
});
