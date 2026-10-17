import { describe, expect, it, vi } from "vitest";
import { createTelegram } from "./bridge";
import type { WebApp } from "./webapp";

function webApp(version: string) {
  const at = (v: string) => {
    const [a = 0, b = 0] = version.split(".").map(Number);
    const [c = 0, d = 0] = v.split(".").map(Number);
    return a > c || (a === c && b >= d);
  };
  const button = { show: vi.fn(), hide: vi.fn(), onClick: vi.fn(), offClick: vi.fn() };
  return {
    initData: "query_id=1&hash=2",
    initDataUnsafe: { start_param: "pack-a" },
    colorScheme: "dark",
    version,
    platform: "ios",
    isVersionAtLeast: at,
    ready: vi.fn(),
    expand: vi.fn(),
    close: vi.fn(),
    setHeaderColor: vi.fn(),
    setBackgroundColor: vi.fn(),
    setBottomBarColor: vi.fn(),
    onEvent: vi.fn(),
    offEvent: vi.fn(),
    openLink: vi.fn(),
    openTelegramLink: vi.fn(),
    disableVerticalSwipes: vi.fn(),
    enableVerticalSwipes: vi.fn(),
    BackButton: { ...button },
    SettingsButton: { ...button, show: vi.fn() },
    HapticFeedback: {
      impactOccurred: vi.fn(),
      notificationOccurred: vi.fn(),
      selectionChanged: vi.fn(),
    },
  } satisfies WebApp;
}

describe("the Telegram bridge", () => {
  it("passes the signed launch data and the startapp of the link", () => {
    const telegram = createTelegram(webApp("8.0"));
    expect(telegram.initData).toBe("query_id=1&hash=2");
    expect(telegram.startParam).toBe("pack-a");
    expect(telegram.colorScheme()).toBe("dark");
  });

  it("paints the header, the background and the bottom bar on a new client", () => {
    const app = webApp("8.0");
    createTelegram(app).paint("#16171c");
    expect(app.setHeaderColor).toHaveBeenCalledWith("#16171c");
    expect(app.setBackgroundColor).toHaveBeenCalledWith("#16171c");
    expect(app.setBottomBarColor).toHaveBeenCalledWith("#16171c");
  });

  it("skips what an old client does not have, instead of failing", () => {
    const app = webApp("6.0");
    const telegram = createTelegram(app);
    telegram.paint("#f4f1ec");
    telegram.setBackButton(true);
    telegram.showSettings();
    telegram.haptic("done");
    telegram.setVerticalSwipes(false);
    expect(app.setHeaderColor).not.toHaveBeenCalled();
    expect(app.BackButton.show).not.toHaveBeenCalled();
    expect(app.SettingsButton.show).not.toHaveBeenCalled();
    expect(app.HapticFeedback.notificationOccurred).not.toHaveBeenCalled();
    expect(app.disableVerticalSwipes).not.toHaveBeenCalled();
  });

  it("maps the haptics of artboard 14 to Telegram's", () => {
    const app = webApp("8.0");
    const telegram = createTelegram(app);
    telegram.haptic("select");
    telegram.haptic("copied");
    telegram.haptic("confirm");
    telegram.haptic("error");
    expect(app.HapticFeedback.selectionChanged).toHaveBeenCalled();
    expect(app.HapticFeedback.impactOccurred).toHaveBeenCalledWith("light");
    expect(app.HapticFeedback.notificationOccurred).toHaveBeenNthCalledWith(1, "warning");
    expect(app.HapticFeedback.notificationOccurred).toHaveBeenNthCalledWith(2, "error");
  });
});
