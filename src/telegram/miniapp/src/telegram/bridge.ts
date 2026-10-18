import type { ColorScheme, WebApp, WebAppEvent } from "./webapp";

/** Haptics of artboard 14 («Вибрация»), by what happened rather than by Telegram's names. */
export type Haptic = "select" | "copied" | "confirm" | "done" | "error";

/** What the Mini App needs from Telegram. Screens talk to this, never to window.Telegram:
 *  tests and the dev bar give them a fake one (fakeTelegram.ts). */
export interface Telegram {
  /** Signed launch data for `Authorization: tma …`; empty outside Telegram */
  readonly initData: string;
  /** `startapp` of the link that opened the Mini App (t.me/StickerSnapAppBot?startapp=…) */
  readonly startParam?: string;
  colorScheme(): ColorScheme;
  /** Telegram switched between its light and dark theme; returns the unsubscribe */
  onThemeChanged(callback: () => void): () => void;
  /** The frame around the Mini App takes this colour, so it matches our background */
  paint(color: string): void;
  ready(): void;
  close(): void;
  /** «Назад» in Telegram's header; hidden means «Закрыть» */
  setBackButton(visible: boolean): void;
  onBack(callback: () => void): () => void;
  /** «Настройки» in the «⋯» menu */
  showSettings(): void;
  onSettings(callback: () => void): () => void;
  haptic(kind: Haptic): void;
  /** t.me links stay inside Telegram: the pack window, the chat picker */
  openTelegramLink(url: string): void;
  /** Leaves Telegram for the browser: only «Скачать StickerSnap» */
  openLink(url: string): void;
  /** The bot may write to the user (launch data's allows_write_to_pm, or a «Разрешить» since) */
  canWrite(): boolean;
  /** Telegram asks the user to let the bot write to them; resolves whether they agreed.
   *  A client too old to ask resolves true: there is nothing to wait for. */
  askWriteAccess(): Promise<boolean>;
  /** Off while a sheet is open: a downward swipe closes the sheet, not the Mini App */
  setVerticalSwipes(enabled: boolean): void;
}

function subscribe(webApp: WebApp, event: WebAppEvent, callback: () => void) {
  webApp.onEvent(event, callback);
  return () => webApp.offEvent(event, callback);
}

/** The real bridge over telegram-web-app.js. Members newer than the client are skipped:
 *  an old client keeps working, just without them. */
export function createTelegram(webApp: WebApp): Telegram {
  const since = (version: string) => webApp.isVersionAtLeast(version);
  let canWrite = webApp.initDataUnsafe.user?.allows_write_to_pm === true;
  return {
    initData: webApp.initData,
    startParam: webApp.initDataUnsafe.start_param,
    colorScheme: () => webApp.colorScheme,
    onThemeChanged: (callback) => subscribe(webApp, "themeChanged", callback),
    paint(color) {
      // A hex header colour came in 6.9; before it only Telegram's own keywords
      if (since("6.9")) webApp.setHeaderColor(color);
      if (since("6.1")) webApp.setBackgroundColor(color);
      if (since("7.10")) webApp.setBottomBarColor?.(color);
    },
    ready() {
      webApp.ready();
      webApp.expand();
    },
    close: () => webApp.close(),
    setBackButton(visible) {
      if (!since("6.1")) return;
      if (visible) webApp.BackButton.show();
      else webApp.BackButton.hide();
    },
    onBack: (callback) => subscribe(webApp, "backButtonClicked", callback),
    showSettings() {
      if (since("7.0")) webApp.SettingsButton.show();
    },
    onSettings: (callback) => subscribe(webApp, "settingsButtonClicked", callback),
    haptic(kind) {
      if (!since("6.1")) return;
      const feedback = webApp.HapticFeedback;
      if (kind === "select") feedback.selectionChanged();
      else if (kind === "copied") feedback.impactOccurred("light");
      else if (kind === "confirm") feedback.notificationOccurred("warning");
      else if (kind === "done") feedback.notificationOccurred("success");
      else feedback.notificationOccurred("error");
    },
    openTelegramLink: (url) => webApp.openTelegramLink(url),
    openLink: (url) => webApp.openLink(url),
    canWrite: () => canWrite,
    askWriteAccess() {
      if (!since("6.9")) return Promise.resolve(true);
      return new Promise((resolve) =>
        webApp.requestWriteAccess((granted) => {
          canWrite ||= granted;
          resolve(granted);
        }),
      );
    },
    setVerticalSwipes(enabled) {
      if (!since("7.7")) return;
      if (enabled) webApp.enableVerticalSwipes();
      else webApp.disableVerticalSwipes();
    },
  };
}
