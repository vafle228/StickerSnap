import { useEffect, useLayoutEffect, useRef } from "react";
import { hasModalLayer } from "@stickersnap/ds/layers";
import { DevicesScreen } from "../features/devices/DevicesScreen";
import { LinkScreen } from "../features/link/LinkScreen";
import { PackScreen } from "../features/packs/PackScreen";
import { PacksScreen } from "../features/packs/PacksScreen";
import { ToastHost } from "../shared/ToastHost";
import type { Telegram } from "../telegram/bridge";
import { useTelegram } from "../telegram/context";
import { useTelegramTheme } from "../telegram/theme";
import { currentScreen, useNav, type Screen } from "./nav";
import { useSession } from "./session";
import { SignedOut } from "./SignedOut";

/** «Назад» in Telegram's header closes the top window or sheet first, the way Esc does,
 *  and only then goes up a screen (artboard 13, rule 2). */
function goBack() {
  if (hasModalLayer()) {
    document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
  } else {
    useNav.getState().back();
  }
}

function useTelegramButtons(telegram: Telegram, depth: number, signedOut: boolean) {
  useEffect(() => telegram.setBackButton(!signedOut && depth > 1), [telegram, depth, signedOut]);
  useEffect(() => telegram.onBack(goBack), [telegram]);
  // «⋯ → Настройки» leads to «Устройства» (rule 3)
  useEffect(
    () => telegram.onSettings(() => useNav.getState().push({ kind: "devices" })),
    [telegram],
  );
}

/** Each depth keeps its scroll, so «Назад» returns to the same place in the list. */
function useScrollPerDepth(depth: number) {
  const saved = useRef<number[]>([]);
  const last = useRef(depth);
  useLayoutEffect(() => {
    if (depth > last.current) {
      saved.current[last.current] = window.scrollY;
      window.scrollTo(0, 0);
    } else if (depth < last.current) {
      window.scrollTo(0, saved.current[depth] ?? 0);
    }
    last.current = depth;
  }, [depth]);
}

function ScreenView({ screen }: { screen: Screen }) {
  switch (screen.kind) {
    case "packs":
      return <PacksScreen />;
    case "pack":
      return <PackScreen key={screen.name} name={screen.name} />;
    case "devices":
      return <DevicesScreen />;
    case "link":
      return <LinkScreen key={screen.code} code={screen.code} />;
  }
}

export function App() {
  const telegram = useTelegram();
  const stack = useNav((s) => s.stack);
  // Outside Telegram there is nothing to sign the requests with
  const signedOut = useSession((s) => s.signedOut) || !telegram.initData;
  useTelegramTheme(telegram);
  useTelegramButtons(telegram, stack.length, signedOut);
  useScrollPerDepth(stack.length);

  if (signedOut) return <SignedOut />;
  return (
    <>
      <ScreenView screen={currentScreen(stack)} />
      <ToastHost />
    </>
  );
}
