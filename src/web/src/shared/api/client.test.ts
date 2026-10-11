import { describe, expect, it } from "vitest";
import { ApiError, call, isProblem } from "./client";
import type { Problem } from "./types";

const reply = <D, E>(status: number, data?: D, error?: E) =>
  Promise.resolve({ data, error, response: new Response(null, { status }) });

const conflict: Problem = {
  type: "https://stickersnap.app/problems/revision_conflict",
  status: 409,
  title: "Revision conflict",
  code: "revision_conflict",
  traceId: "t-1",
};

describe("call", () => {
  it("gives the body of a successful reply", async () => {
    await expect(call(reply(200, { revision: 4 }))).resolves.toEqual({ revision: 4 });
  });

  it("throws the contract's Problem with its code", async () => {
    const error: unknown = await call(reply(409, undefined, conflict)).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 409, problem: { code: "revision_conflict" } });
  });

  it("throws a bare status when the reply has no Problem, like a 401", async () => {
    const error: unknown = await call(reply(401, undefined, "")).catch((e: unknown) => e);
    expect(error).toMatchObject({ status: 401, problem: undefined, message: "HTTP 401" });
  });
});

describe("isProblem", () => {
  it("knows a Problem by its code", () => {
    expect(isProblem(conflict)).toBe(true);
    expect(isProblem({ title: "no code" })).toBe(false);
    expect(isProblem(null)).toBe(false);
    expect(isProblem("revision_conflict")).toBe(false);
  });
});
