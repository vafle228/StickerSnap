// A standalone, offline reference built from the real DS and its existing React specimens.
// No product entry point, Vite dev server, API, mocks or Figma capture code is loaded.
// Usage: npm run build:design-system [-- --check]
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { execFileSync } from "node:child_process";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { build } from "vite";
import ts from "typescript";

const repo = fileURLToPath(new URL("../", import.meta.url));
const read = async (path) => (await readFile(resolve(repo, path), "utf8")).replace(/\r\n/g, "\n");
const target = resolve(repo, "docs/design-system.html");
// Token tables and rendered components must describe the same generated CSS.
execFileSync(process.execPath, [resolve(repo, "tools/build-tokens.mjs"), "--check"], {
  stdio: "inherit",
});
const index = await read("src/ds/src/index.ts");
const tokens = JSON.parse(await read("src/ds/src/tokens.json"));
const examples =
  (await read("src/web/src/showcase/Specimens.tsx")) +
  (await read("src/web/src/showcase/Standalone.tsx"));
const components = [];

for (const match of index.matchAll(
  /export\s*\{\s*(\w+)[^}]*\}\s*from\s*"\.\/components\/([^"]+)"/g,
)) {
  const [, name, module] = match;
  if (!new RegExp(`<${name}(?:\\s|/|>)`).test(examples)) {
    throw new Error(`${name} is exported but has no standalone specimen.`);
  }
  const file = `src/ds/src/components/${module}.tsx`;
  const source = await read(file);
  const ast = ts.createSourceFile(file, source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  const declaration = ast.statements.find(
    (node) => ts.isFunctionDeclaration(node) && node.name?.text === name,
  );
  const description = (declaration ? ts.getJSDocCommentsAndTags(declaration) : [])
    .map((comment) =>
      comment
        .getText(ast)
        .replace(/^\/\*\*|\*\/$/g, "")
        .replace(/^\s*\* ?/gm, ""),
    )
    .join("\n")
    .trim();
  const props = ast.statements
    .filter(
      (node) =>
        (ts.isInterfaceDeclaration(node) || ts.isTypeAliasDeclaration(node)) &&
        node.modifiers?.some((modifier) => modifier.kind === ts.SyntaxKind.ExportKeyword),
    )
    .map((node) => node.getText(ast))
    .join("\n\n");
  components.push({ name, file, description, props });
}

const licenses = await Promise.all(
  [
    ["Manrope · SIL OFL 1.1", "src/ds/fonts/Manrope-OFL.txt"],
    ["JetBrains Mono · SIL OFL 1.1", "src/ds/fonts/JetBrainsMono-OFL.txt"],
    ["React · MIT", "node_modules/react/LICENSE"],
    ["React DOM · MIT", "node_modules/react-dom/LICENSE"],
  ].map(async ([name, file]) => ({ name, file, text: await read(file) })),
);

const result = await build({
  configFile: false,
  root: resolve(repo, "src/web"),
  logLevel: "warn",
  plugins: [
    {
      name: "standalone-normalize-css-lines",
      enforce: "pre",
      // CSS Modules hashes its input; checkout line endings must not change class names.
      transform(code, id) {
        if (id.endsWith(".css")) return code.replace(/\r\n/g, "\n");
      },
    },
  ],
  esbuild: { jsx: "automatic" },
  define: {
    "process.env.NODE_ENV": JSON.stringify("production"),
    __DESIGN_SYSTEM__: JSON.stringify({ tokens, components, licenses }),
  },
  build: {
    write: false,
    target: "es2022",
    sourcemap: false,
    cssCodeSplit: false,
    // Library builds inline font assets; an IIFE also works directly under file://.
    lib: {
      entry: resolve(repo, "src/web/src/showcase/Standalone.tsx"),
      name: "StickerSnapDesignSystem",
      formats: ["iife"],
    },
    rollupOptions: { output: { inlineDynamicImports: true } },
  },
});

const output = (Array.isArray(result) ? result : [result]).flatMap((item) => item.output);
const scripts = output.filter((item) => item.type === "chunk");
const styles = output.filter((item) => item.type === "asset" && item.fileName.endsWith(".css"));
if (scripts.length !== 1 || styles.length !== 1 || output.length !== 2) {
  throw new Error("Expected one inline script and one stylesheet, with no external assets.");
}
const script = scripts[0].code.replace(/<\/script/gi, "<\\/script");
const css = String(styles[0].source);
for (const match of css.matchAll(/url\(([^)]+)\)/g)) {
  if (!/^["']?(?:data:|#)/.test(match[1])) throw new Error(`External CSS asset: ${match[1]}`);
}
if (scripts[0].imports.length || scripts[0].dynamicImports.length) {
  throw new Error("Standalone JavaScript must not import other files.");
}

const html = `<!doctype html>
<!-- Generated from src/ds and src/web/src/showcase. Run npm run build:design-system; do not edit. -->
<html lang="ru" data-theme="light" class="ss-accent-indigo">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="color-scheme" content="light dark">
  <title>StickerSnap — дизайн-система</title>
  <meta name="description" content="Автономная витрина компонентов, токенов и правил StickerSnap.">
  <style>${css.replace(/<\/style/gi, "<\\/style")}</style>
</head>
<body>
  <div id="root"></div>
  <noscript>Для интерактивной витрины нужен JavaScript. Все ресурсы встроены в этот файл.</noscript>
  <script>${script}</script>
</body>
</html>
`;

if (process.argv.includes("--check")) {
  const current = await readFile(target, "utf8").catch(() => "");
  if (current.replace(/\r\n/g, "\n") !== html.replace(/\r\n/g, "\n")) {
    throw new Error("docs/design-system.html is stale: run npm run build:design-system.");
  }
  console.log("docs/design-system.html is up to date and self-contained.");
} else {
  await mkdir(dirname(target), { recursive: true });
  await writeFile(target, html);
  console.log(
    `Wrote docs/design-system.html: ${components.length} components, ${Buffer.byteLength(html)} bytes, no external assets.`,
  );
}
