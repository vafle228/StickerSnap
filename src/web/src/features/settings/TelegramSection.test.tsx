// @vitest-environment happy-dom
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { server, useMockServer } from "../../test/dom";
import { useApp } from "../../app/store";
import { keys } from "../../shared/api/queries";
import { platform } from "../../shared/platform";
import { TelegramSection } from "./TelegramSection";

/* «Публикация в Telegram» (R12, artboards 5 and 18): the four states of the link right in the section. */

function renderSection() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <TelegramSection />
    </QueryClientProvider>,
  );
  return client;
}

const section = () => screen.getByRole("region", { name: "Публикация в Telegram" });

describe("linking the Telegram account", () => {
  it("shows the link to the Mini App with its QR and waits; «Отмена» goes back", async () => {
    useMockServer("unlinked");
    const open = vi.spyOn(platform, "openExternal").mockImplementation(() => {});
    const user = userEvent.setup();
    renderSection();

    await user.click(await screen.findByRole("button", { name: "Привязать" }));

    expect(await screen.findByText("Ждём подтверждения в Telegram")).toBeTruthy();
    expect(screen.getByRole("img", { name: "QR-код ссылки привязки" })).toBeTruthy();
    expect(within(section()).getByRole("timer").textContent).toMatch(
      /^Ссылка действует (10:00|9:5\d)$/,
    );
    // The link waits on screen: Telegram may be only on the phone
    expect(open).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: "Открыть Telegram" }));
    expect(open).toHaveBeenCalledWith(
      expect.stringMatching(/^https:\/\/t\.me\/StickerSnapAppBot\?startapp=link_/),
    );

    await user.click(screen.getByRole("button", { name: "Отмена" }));
    expect(await screen.findByText("Привяжите аккаунт Telegram")).toBeTruthy();
    expect(within(section()).queryByRole("status")).toBeNull();
  });

  it("copies the link", async () => {
    useMockServer("link-wait");
    const user = userEvent.setup();
    const write = vi.spyOn(navigator.clipboard, "writeText").mockResolvedValue();
    renderSection();

    await user.click(await screen.findByRole("button", { name: "Копировать ссылку" }));

    expect(write).toHaveBeenCalledWith(expect.stringContaining("startapp=link_"));
    expect(await screen.findByRole("button", { name: "Скопировано" })).toBeTruthy();
  });

  it.each([
    ["declined", "Привязку отменили в Telegram."],
    ["expired", "Ссылка устарела. Нажмите «Привязать» ещё раз."],
  ] as const)("says why the link ended: %s", async (reason, text) => {
    useMockServer("link-wait");
    const client = renderSection();
    await screen.findByText("Ждём подтверждения в Telegram");

    act(() => {
      client.setQueryData(keys.account, { state: "unlinked", reason });
    });

    expect((await within(section()).findByRole("status")).textContent).toBe(text);
    expect(screen.getByRole("button", { name: "Привязать" })).toBeTruthy();
  });

  it("says «Готово» when the user confirms while the section is open, and leads back to the editor", async () => {
    useMockServer("unlinked");
    vi.spyOn(platform, "openExternal").mockImplementation(() => {});
    const user = userEvent.setup();
    const client = renderSection();
    await user.click(await screen.findByRole("button", { name: "Привязать" }));
    await screen.findByText("Ждём подтверждения в Telegram");
    useApp.getState().setSection("settings");

    // accountChanged, as the hub puts it into the cache
    act(() => {
      client.setQueryData(keys.account, {
        state: "linked",
        user: { id: 42, username: "dima", firstName: "Дима" },
        device: "DESKTOP-7F3K2Q",
      });
    });

    expect(
      await screen.findByText(
        "Компьютер привязан к @dima. Паки будут создаваться на этот аккаунт.",
      ),
    ).toBeTruthy();
    expect(within(section()).getByText("Привязан")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Вернуться в редактор" }));
    expect(useApp.getState().section).toBe("editor");
  });

  it("shows a linked account with «Проверить связь» and unlinks it", async () => {
    useMockServer("ok");
    const user = userEvent.setup();
    renderSection();

    expect(await screen.findByText("@demo, DESKTOP-7F3K2Q")).toBeTruthy();
    expect(screen.queryByText(/привязан\. Паки/)).toBeNull();

    await user.click(screen.getByRole("button", { name: "Отвязать" }));
    expect(await screen.findByText("Привяжите аккаунт Telegram")).toBeTruthy();
  });

  it("tells what keeps the bot from the account and offers to try again", async () => {
    useMockServer("offline");
    renderSection();

    const banner = await screen.findByRole("alert");
    expect(banner.textContent).toContain("Нет связи с Telegram");
    expect(banner.textContent).toContain("Привязка не сбросится");
    expect(within(banner).getByRole("button", { name: "Повторить" })).toBeTruthy();
  });

  it("says in the section when the server has no service", async () => {
    useMockServer("unlinked");
    server.use(
      http.post("*/api/v1/account/link", () =>
        HttpResponse.json(
          {
            type: "about:blank",
            title: "No token",
            status: 503,
            code: "bot_unavailable",
            traceId: "t",
          },
          { status: 503, headers: { "Content-Type": "application/problem+json" } },
        ),
      ),
    );
    const user = userEvent.setup();
    renderSection();

    await user.click(await screen.findByRole("button", { name: "Привязать" }));

    const banner = await screen.findByRole("alert");
    expect(banner.textContent).toContain("Бот StickerSnap не настроен");
    expect(within(banner).getByRole("button", { name: "Повторить" })).toBeTruthy();
  });
});
