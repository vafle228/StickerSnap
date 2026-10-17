import { describe, expect, it } from "vitest";
import { countLabel, gridLabel, isFull, leftLabel, packLink, shareUrl } from "./format";

const pack = {
  name: "reactions_call_by_StickerSnapAppBot",
  title: "Реакции с созвона",
  kind: "stickers" as const,
  count: 12,
  limit: 120,
};

describe("pack words", () => {
  it("count stickers and emoji against their limit", () => {
    expect(countLabel(pack)).toBe("Стикеры · 12 из 120");
    expect(countLabel({ kind: "emoji", count: 18, limit: 200 })).toBe("Эмодзи · 18 из 200");
  });

  it("say a full pack takes nothing more", () => {
    expect(isFull({ count: 120, limit: 120 })).toBe(true);
    expect(gridLabel({ count: 120, limit: 120 })).toBe("120 стикеров · больше Telegram не примет");
    expect(gridLabel(pack)).toBe("12 стикеров");
  });

  it("tell how many stickers are left after a delete", () => {
    expect(leftLabel(11)).toBe("В паке осталось 11 стикеров.");
    expect(leftLabel(1)).toBe("В паке остался 1 стикер.");
    expect(leftLabel(3)).toBe("В паке осталось 3 стикера.");
  });

  it("link stickers to addstickers and emoji to addemoji", () => {
    expect(packLink(pack)).toBe("t.me/addstickers/reactions_call_by_StickerSnapAppBot");
    expect(packLink({ kind: "emoji", name: "chat_by_StickerSnapAppBot" })).toBe(
      "t.me/addemoji/chat_by_StickerSnapAppBot",
    );
  });

  it("share the pack's link through Telegram's chat picker", () => {
    const url = new URL(shareUrl(pack));
    expect(url.origin + url.pathname).toBe("https://t.me/share/url");
    expect(url.searchParams.get("url")).toBe(
      "https://t.me/addstickers/reactions_call_by_StickerSnapAppBot",
    );
    expect(url.searchParams.get("text")).toBe("Реакции с созвона");
  });
});
