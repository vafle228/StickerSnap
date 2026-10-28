import { describe, expect, it } from "vitest";
import { applicationMenu } from "./menu.js";

const roles = (platform: NodeJS.Platform) =>
  (applicationMenu(platform) ?? []).flatMap((menu) =>
    Array.isArray(menu.submenu) ? menu.submenu.map((item) => item.role).filter(Boolean) : [],
  );

describe("the menu bar", () => {
  it("is not there on Windows and Linux", () => {
    expect(applicationMenu("win32")).toBeNull();
    expect(applicationMenu("linux")).toBeNull();
  });

  it("gives a Mac the keys of the fields and of quitting", () => {
    expect(roles("darwin")).toEqual(
      expect.arrayContaining(["undo", "redo", "cut", "copy", "paste", "selectAll", "quit"]),
    );
  });

  it("names every item in Russian", () => {
    const items = (applicationMenu("darwin") ?? []).flatMap((menu) => [
      menu,
      ...(Array.isArray(menu.submenu) ? menu.submenu : []),
    ]);
    const unnamed = items.filter((item) => item.type !== "separator" && !item.label);
    expect(unnamed).toEqual([]);
  });
});
