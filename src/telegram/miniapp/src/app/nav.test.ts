import { beforeEach, describe, expect, it } from "vitest";
import { currentScreen, startStack, useNav } from "./nav";

beforeEach(() => useNav.getState().reset([{ kind: "packs" }]));

describe("screens", () => {
  it("open «Мои паки» first", () => {
    expect(startStack()).toEqual([{ kind: "packs" }]);
  });

  it("open the pack of a startapp link on top of «Мои паки», so «Назад» leads there", () => {
    const stack = startStack("pack-reactions_call_by_StickerSnapAppBot");
    expect(stack).toEqual([
      { kind: "packs" },
      { kind: "pack", name: "reactions_call_by_StickerSnapAppBot" },
    ]);
  });

  it("ignore a startapp they do not know", () => {
    expect(startStack("hello")).toEqual([{ kind: "packs" }]);
    expect(startStack("pack-")).toEqual([{ kind: "packs" }]);
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
