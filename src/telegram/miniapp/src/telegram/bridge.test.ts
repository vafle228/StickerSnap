import { describe, expect, it, vi } from "vitest";
import { createTelegram } from "./bridge";
import type { WebApp } from "./webapp";

function webApp(version: string, { canWrite = false, grants = true } = {}) {
  const at = (v: string) => {
    const [a = 0, b = 0] = version.split(".").map(Number);
    const [c = 0, d = 0] = v.split(".").map(Number);
    return a > c || (a === c && b >= d);
  };
  const button = { show: vi.fn(), hide: vi.fn(), onClick: vi.fn(), offClick: vi.fn() };
  return {
    initData: "query_id=1&hash=2",
    initDataUnsafe: { start_param: "pack_a", user: { allows_write_to_pm: canWrite } },
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
    requestWriteAccess: vi.fn((callback?: (granted: boolean) => void) => callback?.(grants)),
    disableVerticalSwipes: vi.fn(),
    enableVerticalSwipes: vi.fn(),
    enableClosingConfirmation: vi.fn(),
    disableClosingConfirmation: vi.fn(),
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
    expect(telegram.startParam).toBe("pack_a");
    expect(telegram.colorScheme()).toBe("dark");
    expect(telegram.platform).toBe("ios");
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
    telegram.setClosingConfirmation(true);
    expect(app.setHeaderColor).not.toHaveBeenCalled();
    expect(app.BackButton.show).not.toHaveBeenCalled();
    expect(app.SettingsButton.show).not.toHaveBeenCalled();
    expect(app.HapticFeedback.notificationOccurred).not.toHaveBeenCalled();
    expect(app.disableVerticalSwipes).not.toHaveBeenCalled();
    expect(app.enableClosingConfirmation).not.toHaveBeenCalled();
  });

  it("makes Telegram ask before closing while a form holds unsent text", () => {
    const app = webApp("8.0");
    const telegram = createTelegram(app);
    telegram.setClosingConfirmation(true);
    expect(app.enableClosingConfirmation).toHaveBeenCalledOnce();
    telegram.setClosingConfirmation(false);
    expect(app.disableClosingConfirmation).toHaveBeenCalledOnce();
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

  it("knows from the launch data whether the bot may write to the user", () => {
    expect(createTelegram(webApp("8.0", { canWrite: true })).canWrite()).toBe(true);
    expect(createTelegram(webApp("8.0")).canWrite()).toBe(false);
  });

  it("asks Telegram for write access and remembers a «yes»", async () => {
    const app = webApp("8.0");
    const telegram = createTelegram(app);
    expect(await telegram.askWriteAccess()).toBe(true);
    expect(app.requestWriteAccess).toHaveBeenCalledOnce();
    expect(telegram.canWrite()).toBe(true);
  });

  it("reports a «no» and keeps asking next time", async () => {
    const telegram = createTelegram(webApp("8.0", { grants: false }));
    expect(await telegram.askWriteAccess()).toBe(false);
    expect(telegram.canWrite()).toBe(false);
  });

  it("does not wait for an answer a client before 6.9 cannot give", async () => {
    const app = webApp("6.2");
    expect(await createTelegram(app).askWriteAccess()).toBe(true);
    expect(app.requestWriteAccess).not.toHaveBeenCalled();
  });
});
