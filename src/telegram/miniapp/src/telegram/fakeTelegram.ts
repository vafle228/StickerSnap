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
    closingConfirmation: boolean;
    /** How many times Telegram's «Разрешить боту писать?» came up */
    writeAccessAsked: number;
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
  /** «tdesktop» by default */
  platform?: string;
  colorScheme?: ColorScheme;
  /** allows_write_to_pm of the launch data; true by default */
  canWrite?: boolean;
  /** What the user answers Telegram's «Разрешить боту писать?»: each ask takes the next
   *  answer, the last one repeats; «да» by default */
  writeAnswers?: boolean[];
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
    closingConfirmation: false,
    writeAccessAsked: 0,
    closed: false,
    ready: false,
  };
  let canWrite = options.canWrite ?? true;
  const answers = [...(options.writeAnswers ?? [true])];
  const changed = () => stateListeners.forEach((listener) => listener());
  const listen = (set: Set<() => void>, callback: () => void) => {
    set.add(callback);
    return () => void set.delete(callback);
  };

  return {
    initData: options.initData ?? "",
    startParam: options.startParam,
    platform: options.platform ?? "tdesktop",
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
    canWrite: () => canWrite,
    askWriteAccess() {
      calls.writeAccessAsked += 1;
      const granted = (answers.length > 1 ? answers.shift() : answers[0]) ?? true;
      canWrite ||= granted;
      return Promise.resolve(granted);
    },
    setVerticalSwipes: (enabled) => void (calls.verticalSwipes = enabled),
    setClosingConfirmation: (enabled) => void (calls.closingConfirmation = enabled),
    setColorScheme(next) {
      scheme = next;
      themeListeners.forEach((listener) => listener());
    },
    pressBack: () => backListeners.forEach((listener) => listener()),
    pressSettings: () => settingsListeners.forEach((listener) => listener()),
    subscribe: (listener) => listen(stateListeners, listener),
  };
}
