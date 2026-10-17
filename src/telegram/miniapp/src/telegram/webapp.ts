/* The part of window.Telegram.WebApp (telegram-web-app.js, core.telegram.org/bots/webapps) the
   Mini App uses. Typed by hand: the official script ships no types, and these few members are
   all we touch. Methods newer than the client's Bot API version are guarded in bridge.ts. */

export type ColorScheme = "light" | "dark";

export type WebAppEvent =
  "themeChanged" | "backButtonClicked" | "settingsButtonClicked" | "activated";

interface WebAppButton {
  show(): void;
  hide(): void;
  onClick(callback: () => void): void;
  offClick(callback: () => void): void;
}

export interface WebApp {
  /** The signed launch data, sent as is in `Authorization: tma …`; empty outside Telegram */
  readonly initData: string;
  readonly initDataUnsafe: { start_param?: string };
  readonly colorScheme: ColorScheme;
  readonly version: string;
  readonly platform: string;
  isVersionAtLeast(version: string): boolean;
  ready(): void;
  expand(): void;
  close(): void;
  setHeaderColor(color: string): void;
  setBackgroundColor(color: string): void;
  setBottomBarColor?(color: string): void;
  onEvent(event: WebAppEvent, callback: () => void): void;
  offEvent(event: WebAppEvent, callback: () => void): void;
  openLink(url: string): void;
  openTelegramLink(url: string): void;
  disableVerticalSwipes(): void;
  enableVerticalSwipes(): void;
  readonly BackButton: WebAppButton;
  readonly SettingsButton: WebAppButton;
  readonly HapticFeedback: {
    impactOccurred(style: "light" | "medium" | "heavy" | "rigid" | "soft"): void;
    notificationOccurred(type: "error" | "success" | "warning"): void;
    selectionChanged(): void;
  };
}

declare global {
  interface Window {
    Telegram?: { WebApp?: WebApp };
  }
}
