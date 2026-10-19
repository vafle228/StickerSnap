import { describe, expect, it } from "vitest";
import contract from "../../../contract/openapi.yaml?raw";
import type { ErrorCode } from "../shared/api/types";
import { PROBLEMS } from "../shared/notify/problems";
import { problemOf } from "./route";

/** The «code | HTTP | reportable» table in the description of ErrorCode (contract, «Ошибки»). */
function table(): Map<string, { status: number; reportable: boolean }> {
  const rows = contract.matchAll(/^\s*\|\s*([a-z_]+)\s*\|\s*(\d{3})\s*\|\s*(yes|no)\s*\|\s*$/gm);
  return new Map(
    [...rows].map(([, code = "", status = "0", reportable]) => [
      code,
      { status: Number(status), reportable: reportable === "yes" },
    ]),
  );
}

describe("demo errors", () => {
  const codes = Object.keys(PROBLEMS) as ErrorCode[];

  it("answers every error code with the HTTP status and reportable the contract gives it", () => {
    const rows = table();
    expect([...rows.keys()].sort()).toEqual([...codes].sort());
    for (const code of codes) {
      const { status, reportable } = problemOf(code);
      expect([code, { status, reportable }]).toEqual([code, rows.get(code)]);
    }
  });

  it("gives every problem its own trace number", () => {
    expect(problemOf("internal").traceId).not.toBe(problemOf("internal").traceId);
  });
});
