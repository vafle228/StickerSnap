import { describe, expect, it } from "vitest";
import { checkLink, looksLikeLink, recognize, shortLink } from "./parse";

/* The links of artboard 20, section 5, told by their look before any request. */

describe("checkLink", () => {
  it.each([
    ["https://www.youtube.com/watch?v=dQw4w9WgXcQ", "youtube"],
    ["https://youtu.be/dQw4w9WgXcQ?t=12", "youtube"],
    ["youtube.com/shorts/abc123", "youtube"],
    ["https://m.youtube.com/watch?v=abc", "youtube"],
    ["https://music.youtube.com/watch?v=abc", "youtube"],
    ["https://www.youtube.com/playlist?list=PL1", "youtube"],
    ["https://giphy.com/gifs/cat-typing-JIX9t2j0ZTN9S", "giphy-gif"],
    ["https://media.giphy.com/media/JIX9t2j0ZTN9S/giphy.gif", "giphy-gif"],
    ["https://media2.giphy.com/media/JIX9t2j0ZTN9S/giphy.gif", "giphy-gif"],
    ["https://i.giphy.com/JIX9t2j0ZTN9S.gif", "giphy-gif"],
    ["https://giphy.com/stickers/cat-dance-abc", "giphy-sticker"],
  ])("takes %s as %s", (link, kind) => {
    expect(checkLink(link)).toMatchObject({ ok: true, kind });
  });

  it.each(["котик", "", "   ", "просто текст с пробелами", "ftp://youtube.com/watch?v=a"])(
    "says «%s» is not a link",
    (text) => {
      expect(checkLink(text)).toEqual({ ok: false, reason: "not_link" });
    },
  );

  it.each([
    "https://vk.com/video123",
    "https://notyoutube.com/watch?v=a",
    "https://giphy.com/explore/cats",
  ])("says %s is another site", (link) => {
    expect(checkLink(link)).toEqual({ ok: false, reason: "site" });
  });
});

describe("the field's helpers", () => {
  it("badges only a link it takes", () => {
    expect(recognize("https://youtu.be/a")?.site).toBe("youtube");
    expect(recognize("https://vk.com/video1")).toBeUndefined();
  });

  it("leaves Ctrl+V of plain text to the page", () => {
    expect(looksLikeLink("vk.com/video1")).toBe(true);
    expect(looksLikeLink("Кот и коробка")).toBe(false);
  });

  it("shows the link under «Узнаю о видео…» without its scheme and www", () => {
    expect(shortLink("https://www.youtube.com/watch?v=abc")).toBe("youtube.com/watch?v=abc");
    expect(shortLink("youtu.be/abc/")).toBe("youtu.be/abc");
  });
});
