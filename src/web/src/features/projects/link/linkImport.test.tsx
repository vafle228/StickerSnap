// @vitest-environment happy-dom
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useMockServer } from "../../../test/dom";
import { useApp } from "../../../app/store";
import { createFakeHub } from "../../../mocks/fakeHub";
import type { Scenario } from "../../../mocks/scenarios";
import { connectLive } from "../../../shared/api/live";
import { useToasts } from "../../../shared/notify";
import { StartSlot } from "../../editor/start/StartSlot";
import { LinkField } from "./LinkField";
import { openLink, useLinkImport } from "./linkImport";
import { LinkWatch } from "./LinkWatch";

/* Import by link (artboard 20) against the demo server: the field's checks and hints, the
   download in the zone's place, and the popups when no zone shows it. */

let disconnect: (() => void) | undefined;

afterEach(() => {
  disconnect?.();
  disconnect = undefined;
  useLinkImport.setState({ text: "", zones: 0 }, true);
});

/** The app's providers with the demo hub wired into the cache, as useLiveUpdates does. */
function useLiveDemo(scenario: Scenario) {
  vi.useFakeTimers({ shouldAdvanceTime: true });
  const db = useMockServer(scenario);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const hub = createFakeHub();
  disconnect = connectLive(client, hub);
  hub.start();
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>
      {children}
      <LinkWatch />
    </QueryClientProvider>
  );
  const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
  return { db, client, wrapper, user };
}

const field = () => screen.getByRole("textbox", { name: "Ссылка на видео" });
const advance = (ms: number) => act(() => vi.advanceTimersByTimeAsync(ms));

describe("the link field (3·1–3·3)", () => {
  it("keeps «Открыть» off while empty and badges a YouTube link as it is typed", async () => {
    const { wrapper, user } = useLiveDemo("ok");
    render(<LinkField />, { wrapper });

    expect(screen.getByRole("button", { name: "Открыть" })).toHaveProperty("disabled", true);
    await user.type(field(), "https://youtu.be/abc");

    expect(screen.getByRole("button", { name: "Открыть" })).toHaveProperty("disabled", false);
    expect(screen.getByText("YouTube")).toBeTruthy();
    expect(screen.getByText("Видео с YouTube")).toBeTruthy();
  });

  it("says under the field what is wrong at «Открыть», and forgets it on the next key", async () => {
    const { wrapper, user } = useLiveDemo("ok");
    render(<LinkField />, { wrapper });

    await user.type(field(), "котик{Enter}");
    expect(screen.getByText("Это не ссылка")).toBeTruthy();

    await user.clear(field());
    await user.type(field(), "https://vk.com/video1{Enter}");
    expect(screen.getByText("Пока умею только YouTube и Giphy")).toBeTruthy();
    expect(field().getAttribute("aria-invalid")).toBe("true");

    await user.type(field(), "2");
    expect(screen.queryByText("Пока умею только YouTube и Giphy")).toBeNull();
  });

  it("puts the server's refusal of a playlist under the field", async () => {
    const { wrapper, user } = useLiveDemo("ok");
    render(<LinkField />, { wrapper });

    await user.type(field(), "https://www.youtube.com/playlist?list=PL1{Enter}");
    await advance(1000);

    expect(screen.getByText("Это плейлист: нужна ссылка на одно видео")).toBeTruthy();
    expect(useToasts.getState().toasts).toHaveLength(0);
  });
});

describe("the download in the zone's place (3·4–3·9)", () => {
  it("asks about the video, downloads it and opens the project named after it", async () => {
    const { db, wrapper, user } = useLiveDemo("ok");
    render(<StartSlot />, { wrapper });

    await user.type(field(), "https://www.youtube.com/watch?v=abc{Enter}");
    await advance(400);
    expect(screen.getByRole("status").textContent).toContain("Узнаю о видео…");

    await advance(1500);
    const card = screen.getByRole("status").textContent ?? "";
    expect(card).toContain("Кот смотрит в окно");
    expect(card).toContain("YouTube · 0:48 · скачиваю 720p");
    expect(card).not.toContain("Ролик длинный");

    await advance(12_000);
    const project = db.projects[0];
    expect(project?.name).toBe("Кот смотрит в окно");
    expect(useApp.getState().projectId).toBe(project?.id);
    expect(useLinkImport.getState().text).toBe("");
  });

  it("brings the zone back with the link still in the field after «Отменить»", async () => {
    const { db, wrapper, user } = useLiveDemo("ok");
    const before = db.projects.length;
    render(<StartSlot />, { wrapper });

    await user.type(field(), "https://youtu.be/abc{Enter}");
    await advance(2000);
    await user.click(screen.getByRole("button", { name: "Отменить" }));
    await advance(100);

    expect(field()).toHaveProperty("value", "https://youtu.be/abc");
    expect(db.downloads[0]?.state).toBe("canceled");
    await advance(12_000);
    expect(db.projects).toHaveLength(before);
    expect(useToasts.getState().toasts).toHaveLength(0);
  });

  it("downloads a long video anyway, with the yellow line in its card", async () => {
    const { wrapper, user } = useLiveDemo("link-long");
    render(<StartSlot />, { wrapper });

    await user.type(field(), "https://youtu.be/stream{Enter}");
    await advance(2000);

    expect(screen.getByRole("status").textContent).toContain(
      "Ролик длинный: 1:12:40, около 410 МБ. Ждать придётся долго",
    );
  });
});

describe("popups (section 4)", () => {
  it("carries a download no zone shows in a popup, then offers «Открыть»", async () => {
    const { db, client, wrapper } = useLiveDemo("ok");
    render(<div />, { wrapper });

    await act(() => openLink(client, "https://giphy.com/gifs/cat-typing-abc"));
    await advance(600);
    const progress = useToasts.getState().toasts.find((t) => t.progress !== undefined);
    expect(progress?.title).toBe("Скачиваю «Кот печатает на клавиатуре»");
    expect(progress?.description).toBe("Giphy · MP4");

    await advance(12_000);
    const done = useToasts.getState().toasts.find((t) => !t.leaving);
    expect(done?.title).toBe("Видео скачано");
    expect(useApp.getState().projectId).toBeUndefined();
    act(() => done?.action?.onClick());
    expect(useApp.getState().projectId).toBe(db.projects[0]?.id);
  });

  it("says there is no room for the video", async () => {
    const { client, wrapper } = useLiveDemo("link-disk");
    render(<div />, { wrapper });

    await act(() => openLink(client, "https://youtu.be/abc"));
    await advance(500);

    const toast = useToasts.getState().toasts.find((t) => !t.leaving);
    expect(toast?.title).toBe("Не хватает места для ролика");
    expect(toast?.description).toBe("Нужно 38 МБ, свободно 12 МБ. Освободите место и повторите.");
  });

  it("offers «Повторить» when the site cannot be reached", async () => {
    const { client, wrapper } = useLiveDemo("offline");
    render(<div />, { wrapper });

    await act(() => openLink(client, "https://youtu.be/abc"));
    await advance(1000);

    const toast = useToasts.getState().toasts[0];
    expect(toast?.title).toBe("Не удалось скачать видео");
    expect(toast?.description).toBe("Нет связи с YouTube. Проверьте интернет.");
    expect(toast?.action?.label).toBe("Повторить");
  });

  it("offers «Сообщить» when the downloader no longer understands the site", async () => {
    const { client, wrapper } = useLiveDemo("link-broken");
    render(<div />, { wrapper });

    await act(() => openLink(client, "https://youtu.be/abc"));
    await advance(6000);

    const toast = useToasts.getState().toasts.find((t) => !t.leaving && t.tone === "danger");
    expect(toast?.title).toBe("YouTube не отдаёт видео");
    expect(toast?.report).toBeDefined();
  });
});
