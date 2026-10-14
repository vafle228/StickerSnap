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

/* «Публикация в Telegram» (R12, artboard 5): the four states of the link right in the section. */

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
  it("opens the bot with a one-time link and waits for Start; «Отмена» goes back", async () => {
    useMockServer("unlinked");
    const open = vi.spyOn(platform, "openExternal").mockImplementation(() => {});
    const user = userEvent.setup();
    renderSection();

    await user.click(await screen.findByRole("button", { name: "Открыть бота в Telegram" }));

    expect(await screen.findByText("Нажмите «Start» в боте")).toBeTruthy();
    expect(within(section()).getByRole("status").textContent).toContain("Ждём ответа от бота…");
    expect(open).toHaveBeenCalledWith(
      expect.stringMatching(/^https:\/\/t\.me\/StickerSnapAppBot\?start=/),
    );

    await user.click(screen.getByRole("button", { name: "Открыть бота ещё раз" }));
    expect(open).toHaveBeenCalledTimes(2);

    await user.click(screen.getByRole("button", { name: "Отмена" }));
    expect(await screen.findByText("Привяжите аккаунт Telegram")).toBeTruthy();
  });

  it("says «Готово» when Start comes while the section is open, and leads back to the editor", async () => {
    useMockServer("unlinked");
    vi.spyOn(platform, "openExternal").mockImplementation(() => {});
    const user = userEvent.setup();
    const client = renderSection();
    await user.click(await screen.findByRole("button", { name: "Открыть бота в Telegram" }));
    await screen.findByText("Нажмите «Start» в боте");
    useApp.getState().setSection("settings");

    // accountChanged, as the hub puts it into the cache
    act(() => {
      client.setQueryData(keys.account, {
        state: "linked",
        user: { id: 42, username: "dima", firstName: "Дима" },
      });
    });

    expect(
      await screen.findByText("Аккаунт @dima привязан. Паки будут создаваться на него."),
    ).toBeTruthy();
    expect(within(section()).getByText("Привязан")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Вернуться в редактор" }));
    expect(useApp.getState().section).toBe("editor");
  });

  it("shows a linked account with «Проверить связь» and unlinks it", async () => {
    useMockServer("ok");
    const user = userEvent.setup();
    renderSection();

    expect(await screen.findByText("@demo")).toBeTruthy();
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

  it("says in the section when the server has no bot token", async () => {
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

    await user.click(await screen.findByRole("button", { name: "Открыть бота в Telegram" }));

    const banner = await screen.findByRole("alert");
    expect(banner.textContent).toContain("Бот StickerSnap не настроен");
    expect(within(banner).getByRole("button", { name: "Повторить" })).toBeTruthy();
  });
});
