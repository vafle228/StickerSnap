import { create } from "zustand";

/* No router (frontend stack): the Mini App is a stack of screens, and «Назад» in Telegram's
   header pops it. Screens go one level deeper at a time (artboard 13, «Откуда открывается»). */

export type Screen =
  | { kind: "packs" }
  | { kind: "pack"; name: string }
  | { kind: "devices" }
  | { kind: "link"; code: string };

const ROOT: Screen = { kind: "packs" };

/** What the `startapp` of a t.me/StickerSnapAppBot link opens (the bot's buttons and the
 *  desktop's «Привязать»): `pack_<name>`, `devices`, `link_<code>`, `feedback`. */
export const STARTAPP = { pack: "pack_", link: "link_", devices: "devices" } as const;

const after = (param: string, prefix: string) =>
  param.startsWith(prefix) ? param.slice(prefix.length) : "";

/** The stack a launch starts with. A pack or «Устройства» sits on top of «Мои паки», so that
 *  «Назад» still leads there. «Привязать компьютер?» stands alone (18·1): Telegram shows
 *  «Закрыть», and its outcomes lead on to «Мои паки». `feedback` opens «Мои паки» until the
 *  Mini App has the feedback form (F3); anything unknown does too. */
export function startStack(startParam = ""): Screen[] {
  const code = after(startParam, STARTAPP.link);
  if (code) return [{ kind: "link", code }];
  const name = after(startParam, STARTAPP.pack);
  if (name) return [ROOT, { kind: "pack", name }];
  if (startParam === STARTAPP.devices) return [ROOT, { kind: "devices" }];
  return [ROOT];
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

const same = (a: Screen, b: Screen) => JSON.stringify(a) === JSON.stringify(b);

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
