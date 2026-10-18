import { beforeEach, describe, expect, it } from "vitest";
import { currentScreen, startStack, useNav } from "./nav";

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

  it("open «Мои паки» for the feedback form until the Mini App has it", () => {
    expect(startStack("feedback")).toEqual([{ kind: "packs" }]);
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
