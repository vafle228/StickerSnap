import js from "@eslint/js";
import globals from "globals";
import tseslint from "typescript-eslint";

export default tseslint.config(
  { ignores: ["dist", "release", "server-dist"] },
  {
    extends: [js.configs.recommended, ...tseslint.configs.strict],
    files: ["**/*.{ts,cts}"],
    languageOptions: {
      ecmaVersion: 2023,
      globals: globals.node,
    },
  },
  {
    // The sandboxed preload is CommonJS: `import x = require()` is how TypeScript writes that.
    files: ["**/*.cts"],
    rules: { "@typescript-eslint/no-require-imports": "off" },
  },
);
