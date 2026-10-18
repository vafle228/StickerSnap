import { beforeEach, describe, expect, it } from "vitest";
import { backOrAsk, currentScreen, guardBack, startStack, useNav } from "./nav";

beforeEach(() => useNav.getState().reset([{ kind: "packs" }]));

describe("screens", () => {
  it("open «Мои паки» first", () => {
    expect(startStack()).toEqual([{ kind: "packs" }]);
  });

  it("open the pack of a startapp link on top of «Мои паки», so «Назад» leads there", () => {
    const stack = startStack("pack_reactions_call_by_StickerSnapAppBot");
    expect(stack).toEqual([
      { kind: "packs" },
      { kind: "pack", name: "reactions_call_by_StickerSnapAppBot" },
    ]);
  });

  it("open «Устройства» on top of «Мои паки» from the bot's «Устройства»", () => {
    expect(startStack("devices")).toEqual([{ kind: "packs" }, { kind: "devices" }]);
  });

  it("open «Привязать компьютер?» alone, so Telegram shows «Закрыть» on it", () => {
    expect(startStack("link_Ab-9_x")).toEqual([{ kind: "link", code: "Ab-9_x" }]);
  });

  it("open «Отзыв» on top of «Мои паки» from the bot's «Написать отзыв» and /feedback", () => {
    expect(startStack("feedback")).toEqual([{ kind: "packs" }, { kind: "feedback" }]);
  });

  it("ignore a startapp they do not know", () => {
    expect(startStack("hello")).toEqual([{ kind: "packs" }]);
    expect(startStack("pack_")).toEqual([{ kind: "packs" }]);
    expect(startStack("link_")).toEqual([{ kind: "packs" }]);
    // the old form, before the bot settled on pack_<name>
    expect(startStack("pack-cat_by_StickerSnapAppBot")).toEqual([{ kind: "packs" }]);
  });

  it("go one level deeper at a time and back one level", () => {
    const nav = useNav.getState();
    nav.push({ kind: "pack", name: "a" });
    expect(currentScreen(useNav.getState().stack)).toEqual({ kind: "pack", name: "a" });
    nav.back();
    expect(useNav.getState().stack).toEqual([{ kind: "packs" }]);
  });

  it("never pop «Мои паки»", () => {
    useNav.getState().back();
    expect(useNav.getState().stack).toEqual([{ kind: "packs" }]);
  });

  it("do not stack «Устройства» twice when «Настройки» is pressed on it", () => {
    const nav = useNav.getState();
    nav.push({ kind: "devices" });
    nav.push({ kind: "devices" });
    expect(useNav.getState().stack).toHaveLength(2);
  });
});

describe("«Назад»", () => {
  it("goes up a level when no screen guards it", () => {
    useNav.getState().push({ kind: "feedback" });
    backOrAsk();
    expect(useNav.getState().stack).toEqual([{ kind: "packs" }]);
  });

  it("asks the guarding screen instead, until it lets go", () => {
    useNav.getState().push({ kind: "feedback" });
    let asked = 0;
    const release = guardBack(() => (asked += 1));
    backOrAsk();
    expect(asked).toBe(1);
    expect(useNav.getState().stack).toHaveLength(2);

    release();
    backOrAsk();
    expect(asked).toBe(1);
    expect(useNav.getState().stack).toEqual([{ kind: "packs" }]);
  });

  it("keeps a newer guard when an older one lets go", () => {
    let newer = 0;
    const older = guardBack(() => undefined);
    const release = guardBack(() => (newer += 1));
    older();
    backOrAsk();
    expect(newer).toBe(1);
    release();
  });
});
