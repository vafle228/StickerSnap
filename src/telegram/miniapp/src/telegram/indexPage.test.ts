import { describe, expect, it } from "vitest";
import page from "../../index.html?raw";

describe("the Mini App page", () => {
  // Telegram's web view loads the page past the MTProto proxy: a script from telegram.org (or any
  // other host) hangs the page where that host is blocked, and the Mini App starts signed out
  it("loads its scripts from its own domain only", () => {
    const sources = [...page.matchAll(/<script[^>]*\ssrc="([^"]+)"/g)].map(
      (match) => match[1] ?? "",
    );
    expect(sources).toContain("/telegram-web-app.js");
    expect(sources.filter((source) => !source.startsWith("/"))).toEqual([]);
  });
});
