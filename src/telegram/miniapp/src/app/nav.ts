import { create } from "zustand";

/* No router (frontend stack): the Mini App is a stack of screens, and «Назад» in Telegram's
   header pops it. Screens go one level deeper at a time (artboard 13, «Откуда открывается»). */

export type Screen = { kind: "packs" } | { kind: "pack"; name: string } | { kind: "devices" };

/** t.me/StickerSnapAppBot?startapp=pack-<name> opens a pack right away (a bot notification) */
export const PACK_LINK_PREFIX = "pack-";

const ROOT: Screen = { kind: "packs" };

/** The stack a launch starts with: «Мои паки», with the pack of the link on top of it, so that
 *  «Назад» from a pack opened by link still leads to «Мои паки». */
export function startStack(startParam?: string): Screen[] {
  const name = startParam?.startsWith(PACK_LINK_PREFIX)
    ? startParam.slice(PACK_LINK_PREFIX.length)
    : "";
  return name ? [ROOT, { kind: "pack", name }] : [ROOT];
}

interface Nav {
  stack: Screen[];
  push(screen: Screen): void;
  /** One level up; «Мои паки» stays */
  back(): void;
  /** Back to «Мои паки»: the pack is gone */
  home(): void;
  reset(stack: Screen[]): void;
}

const same = (a: Screen, b: Screen) =>
  a.kind === b.kind && (a.kind !== "pack" || (b.kind === "pack" && a.name === b.name));

export const useNav = create<Nav>()((set) => ({
  stack: [ROOT],
  push: (screen) =>
    set(({ stack }) => {
      const top = stack[stack.length - 1];
      return top && same(top, screen) ? { stack } : { stack: [...stack, screen] };
    }),
  back: () => set(({ stack }) => ({ stack: stack.length > 1 ? stack.slice(0, -1) : stack })),
  home: () => set({ stack: [ROOT] }),
  reset: (stack) => set({ stack: stack.length > 0 ? stack : [ROOT] }),
}));

export function currentScreen(stack: Screen[]): Screen {
  return stack[stack.length - 1] ?? ROOT;
}
