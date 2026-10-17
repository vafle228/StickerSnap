import type { Haptic, Telegram } from "./bridge";
import type { ColorScheme } from "./webapp";

/** A Telegram without Telegram: for tests, and for the dev server in a plain browser
 *  (?mock), where the dev bar plays the header's «Назад» and «⋯ → Настройки». */
export interface FakeTelegram extends Telegram {
  readonly calls: {
    backButton: boolean;
    settingsShown: boolean;
    painted: string[];
    haptics: Haptic[];
    telegramLinks: string[];
    links: string[];
    verticalSwipes: boolean;
    closed: boolean;
    ready: boolean;
  };
  setColorScheme(scheme: ColorScheme): void;
  pressBack(): void;
  pressSettings(): void;
  /** The dev bar re-renders on these */
  subscribe(listener: () => void): () => void;
}

export interface FakeTelegramOptions {
  initData?: string;
  startParam?: string;
  colorScheme?: ColorScheme;
}

export function createFakeTelegram(options: FakeTelegramOptions = {}): FakeTelegram {
  let scheme: ColorScheme = options.colorScheme ?? "light";
  const themeListeners = new Set<() => void>();
  const backListeners = new Set<() => void>();
  const settingsListeners = new Set<() => void>();
  const stateListeners = new Set<() => void>();
  const calls: FakeTelegram["calls"] = {
    backButton: false,
    settingsShown: false,
    painted: [],
    haptics: [],
    telegramLinks: [],
    links: [],
    verticalSwipes: true,
    closed: false,
    ready: false,
  };
  const changed = () => stateListeners.forEach((listener) => listener());
  const listen = (set: Set<() => void>, callback: () => void) => {
    set.add(callback);
    return () => void set.delete(callback);
  };

  return {
    initData: options.initData ?? "",
    startParam: options.startParam,
    calls,
    colorScheme: () => scheme,
    onThemeChanged: (callback) => listen(themeListeners, callback),
    paint: (color) => void calls.painted.push(color),
    ready: () => void (calls.ready = true),
    close: () => void (calls.closed = true),
    setBackButton(visible) {
      calls.backButton = visible;
      changed();
    },
    onBack: (callback) => listen(backListeners, callback),
    showSettings() {
      calls.settingsShown = true;
      changed();
    },
    onSettings: (callback) => listen(settingsListeners, callback),
    haptic: (kind) => void calls.haptics.push(kind),
    openTelegramLink: (url) => void calls.telegramLinks.push(url),
    openLink: (url) => void calls.links.push(url),
    setVerticalSwipes: (enabled) => void (calls.verticalSwipes = enabled),
    setColorScheme(next) {
      scheme = next;
      themeListeners.forEach((listener) => listener());
    },
    pressBack: () => backListeners.forEach((listener) => listener()),
    pressSettings: () => settingsListeners.forEach((listener) => listener()),
    subscribe: (listener) => listen(stateListeners, listener),
  };
}
