import js from "@eslint/js";
import globals from "globals";
import reactHooks from "eslint-plugin-react-hooks";
import reactRefresh from "eslint-plugin-react-refresh";
import tseslint from "typescript-eslint";

// The same rules as src/web: the apps and their design system read alike.
export default tseslint.config({
  extends: [js.configs.recommended, ...tseslint.configs.strict],
  files: ["**/*.{ts,tsx}"],
  languageOptions: {
    ecmaVersion: 2022,
    globals: globals.browser,
  },
  plugins: {
    "react-hooks": reactHooks,
    "react-refresh": reactRefresh,
  },
  rules: {
    ...reactHooks.configs.recommended.rules,
    "react-refresh/only-export-components": ["warn", { allowConstantExport: true }],
  },
});
