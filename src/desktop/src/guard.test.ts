import { describe, expect, it } from "vitest";
import { isAppUrl, isExternalLink, isServerFile, safeFileName } from "./guard.js";

describe("what the window may reach", () => {
  it("opens web pages and Telegram links in the system, nothing else", () => {
    expect(isExternalLink("https://t.me/addstickers/cats")).toBe(true);
    expect(isExternalLink("tg://addstickers?set=cats")).toBe(true);
    expect(isExternalLink("file:///C:/Windows/system32/calc.exe")).toBe(false);
    expect(isExternalLink("javascript:alert(1)")).toBe(false);
    expect(isExternalLink("ms-settings:privacy")).toBe(false);
    expect(isExternalLink("not a url")).toBe(false);
  });

  it("stays on the app's own origin, port included", () => {
    expect(isAppUrl("http://127.0.0.1:5240/?mock", "http://127.0.0.1:5240")).toBe(true);
    expect(isAppUrl("http://127.0.0.1:5241/", "http://127.0.0.1:5240")).toBe(false);
    expect(isAppUrl("https://example.com/", "http://127.0.0.1:5240")).toBe(false);
  });

  it("saves only rendered files of the local server, so the token goes nowhere else", () => {
    const origin = "http://127.0.0.1:5240";
    expect(isServerFile(`${origin}/api/v1/files/abc`, origin)).toBe(true);
    expect(isServerFile(`${origin}/api/v1/projects`, origin)).toBe(false);
    expect(isServerFile("https://evil.example/api/v1/files/abc", origin)).toBe(false);
  });

  it("saves under a plain file name", () => {
    expect(safeFileName("Стикер 1.webm", "sticker.webm")).toBe("Стикер 1.webm");
    expect(safeFileName("../../evil.webm", "sticker.webm")).toBe("evil.webm");
    expect(safeFileName("C:\\x\\a:b?.webm", "sticker.webm")).toBe("a_b_.webm");
    expect(safeFileName("...", "sticker.webm")).toBe("sticker.webm");
  });
});
