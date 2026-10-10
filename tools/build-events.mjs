// openapi.yaml x-events → src/web/src/shared/api/events.ts, the typed list of SignalR events.
// OpenAPI does not describe hubs, so the contract keeps them in the x-events extension
// and their payloads in components/schemas.
// Usage: node tools/build-events.mjs [--check]
// --check fails when the committed events.ts is stale (used by CI).
import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import yaml from "js-yaml";

const source = fileURLToPath(new URL("../src/contract/openapi.yaml", import.meta.url));
const target = fileURLToPath(new URL("../src/web/src/shared/api/events.ts", import.meta.url));
const spec = yaml.load(readFileSync(source, "utf8"));
const { hub, events } = spec["x-events"];

const schemaOf = (name, payload) => {
  const match = /^#\/components\/schemas\/(\w+)$/.exec(payload?.$ref ?? "");
  if (!match || !spec.components.schemas[match[1]]) {
    throw new Error(`x-events.${name}: payload must $ref an existing components/schemas entry`);
  }
  return match[1];
};

const entries = Object.entries(events).map(([name, e]) => ({
  name,
  schema: schemaOf(name, e.payload),
  ...e,
}));

const ts = `// Generated from src/contract/openapi.yaml (x-events) by tools/build-events.mjs. Do not edit.
import type { components } from "./schema";

type Schemas = components["schemas"];

export const HUB_PATH = ${JSON.stringify(hub)};

/** SignalR events of the hub: the server sends a whole snapshot, never a diff. */
export interface HubEvents {
${entries
  .map(
    (e) => `  /** ${e.when}. Updates: ${e.updates} */
  ${e.name}: Schemas[${JSON.stringify(e.schema)}];`,
  )
  .join("\n")}
}

export type HubEvent = keyof HubEvents;

export const HUB_EVENTS: readonly HubEvent[] = [${entries.map((e) => JSON.stringify(e.name)).join(", ")}];
`;

if (process.argv.includes("--check")) {
  let current = "";
  try {
    current = readFileSync(target, "utf8");
  } catch {
    // missing file is stale too
  }
  if (current.replace(/\r\n/g, "\n") !== ts) {
    console.error("events.ts is out of date: run `npm run gen:api` and commit the result.");
    process.exit(1);
  }
  console.log("events.ts is up to date.");
} else {
  writeFileSync(target, ts);
  console.log(`Wrote ${target}`);
}
