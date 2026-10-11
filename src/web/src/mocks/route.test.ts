import { describe, expect, it } from "vitest";
import contract from "../../../contract/openapi.yaml?raw";
import type { ErrorCode } from "../shared/api/types";
import { PROBLEMS } from "../shared/notify/problems";
import { problemOf } from "./route";

/** The «code | HTTP» table in the description of ErrorCode (contract, «Ошибки»). */
function statusTable(): Map<string, number> {
  const rows = contract.matchAll(/^\s*\|\s*([a-z_]+)\s*\|\s*(\d{3})\s*\|\s*$/gm);
  return new Map([...rows].map(([, code = "", status = "0"]) => [code, Number(status)]));
}

describe("demo errors", () => {
  const codes = Object.keys(PROBLEMS) as ErrorCode[];

  it("answers every error code with the HTTP status the contract gives it", () => {
    const table = statusTable();
    expect([...table.keys()].sort()).toEqual([...codes].sort());
    for (const code of codes)
      expect([code, problemOf(code).status]).toEqual([code, table.get(code)]);
  });

  it("gives every problem its own trace number", () => {
    expect(problemOf("internal").traceId).not.toBe(problemOf("internal").traceId);
  });
});
