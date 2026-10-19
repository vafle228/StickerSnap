// @vitest-environment happy-dom
import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { server, useMockServer, withProviders } from "../../test/dom";
import type { FeedbackRequest } from "../../shared/api/types";
import { useToasts } from "../../shared/notify";
import { OverlayHost } from "../../shared/overlays";
import { AboutSection } from "../settings/AboutSection";
import { openFeedback } from "./openFeedback";

/* «Написать отзыв» (artboard 19·4) against the demo server: the type, the empty text, the
   answer by the bot or at a contact, the report of a bug, and what a failure leaves in the window. */

function sent(): FeedbackRequest[] {
  const bodies: FeedbackRequest[] = [];
  server.events.on("request:start", ({ request }) => {
    if (request.method === "POST" && new URL(request.url).pathname === "/api/v1/feedback") {
      void request
        .clone()
        .json()
        .then((body) => bodies.push(body as FeedbackRequest));
    }
  });
  return bodies;
}

function open() {
  const Wrapper = withProviders();
  render(
    <Wrapper>
      <OverlayHost />
    </Wrapper>,
  );
  act(() => openFeedback());
  return screen.getByRole("dialog", { name: "Написать отзыв" });
}

/** The popup «Отзыв отправлен» (NotifyHost is not rendered here: the store holds it) */
async function sentToast() {
  await waitFor(() => expect(useToasts.getState().toasts[0]?.title).toBe("Отзыв отправлен"));
}

/** The window has faded out */
async function closed() {
  await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
}

const field = (window: HTMLElement) => within(window).getByRole("textbox", { name: /Что/ });

describe("«Написать отзыв»", () => {
  it("opens as an idea, asks for a few words, and sends; the bot answers a linked computer", async () => {
    useMockServer("ok");
    const bodies = sent();
    const user = userEvent.setup();
    const window = open();

    expect(within(window).getByRole("radio", { name: "Идея", checked: true })).toBeTruthy();
    await user.click(within(window).getByRole("button", { name: "Отправить" }));
    expect(within(window).getByText("Напишите хотя бы пару слов")).toBeTruthy();

    await user.type(field(window), "Обрезать на таймлайне");
    expect(within(window).getByText("Необходимо · 21 / 2000")).toBeTruthy();
    expect(
      await within(window).findByRole("checkbox", { name: /Можно ответить мне в Telegram/ }),
    ).toBeTruthy();
    await user.click(within(window).getByRole("button", { name: "Отправить" }));

    await sentToast();
    await closed();
    expect(bodies).toEqual([{ type: "idea", text: "Обрезать на таймлайне", canReply: true }]);
    expect(useToasts.getState().toasts[0]?.description).toBe(
      "Спасибо! Если разрешили, ответим в Telegram.",
    );
  });

  it("asks where to answer when the computer is not linked, and sends the contact", async () => {
    useMockServer("unlinked");
    const bodies = sent();
    const user = userEvent.setup();
    const window = open();

    await user.type(field(window), "Идея");
    const contact = await within(window).findByRole("textbox", { name: "Куда ответить" });
    await user.type(contact, "@dima");
    await user.click(within(window).getByRole("button", { name: "Отправить" }));

    await sentToast();
    expect(bodies).toEqual([{ type: "idea", text: "Идея", canReply: true, contact: "@dima" }]);
  });

  it("dims the contact when the answer is not wanted, and promises none", async () => {
    useMockServer("unlinked");
    const bodies = sent();
    const user = userEvent.setup();
    const window = open();

    await user.type(field(window), "Просто спасибо");
    await user.click(await within(window).findByRole("checkbox", { name: "Ответить мне на" }));
    expect(within(window).getByRole("textbox", { name: "Куда ответить" })).toHaveProperty(
      "disabled",
      true,
    );
    await user.click(within(window).getByRole("button", { name: "Отправить" }));

    await sentToast();
    expect(bodies).toEqual([{ type: "idea", text: "Просто спасибо", canReply: false }]);
  });

  it("says what goes with a bug: the note always, the list behind «Что отправится»", async () => {
    useMockServer("ok");
    const user = userEvent.setup();
    const window = open();

    await user.click(within(window).getByRole("radio", { name: "Ошибка" }));
    expect(within(window).getByText(/К ошибке приложим сведения/)).toBeTruthy();
    expect(within(window).getByRole("textbox", { name: "Что случилось" })).toBeTruthy();
    expect(within(window).queryByText("Версии: StickerSnap, FFmpeg, .NET, ОС")).toBeNull();

    await user.click(within(window).getByRole("button", { name: "Что отправится" }));
    expect(within(window).getByText("Версии: StickerSnap, FFmpeg, .NET, ОС")).toBeTruthy();

    await user.click(within(window).getByRole("radio", { name: "Идея" }));
    expect(within(window).queryByText(/К ошибке приложим сведения/)).toBeNull();
  });

  it("keeps the window and the text without a network, and «Повторить» sends again", async () => {
    const db = useMockServer("offline");
    const user = userEvent.setup();
    const window = open();

    await user.click(within(window).getByRole("radio", { name: "Другое" }));
    await user.type(field(window), "Привет");
    await user.click(within(window).getByRole("button", { name: "Отправить" }));

    expect(await within(window).findByText("Нет связи. Текст сохранён")).toBeTruthy();
    expect((field(window) as HTMLTextAreaElement).value).toBe("Привет");

    // The network is back
    server.resetHandlers(
      ...(await import("../../mocks/handlers")).handlers({ ...db, scenario: "ok" }),
    );
    await user.click(within(window).getByRole("button", { name: "Повторить" }));
    await sentToast();
  });

  it("says when the next one can go after five in an hour, with nothing to retry", async () => {
    useMockServer("feedback-limit");
    const user = userEvent.setup();
    const window = open();

    await user.type(field(window), "Шестой");
    await user.click(within(window).getByRole("button", { name: "Отправить" }));

    expect(await within(window).findByText("Слишком много отзывов подряд")).toBeTruthy();
    expect(within(window).getByText("Следующий можно отправить через 60 мин.")).toBeTruthy();
    expect(within(window).queryByRole("button", { name: "Повторить" })).toBeNull();
  });

  it("asks before dropping a written text, and closes an empty window at once", async () => {
    useMockServer("ok");
    const user = userEvent.setup();
    let window = open();

    await user.type(field(window), "Черновик");
    await user.click(within(window).getByRole("button", { name: "Отмена" }));
    const ask = await screen.findByRole("alertdialog", { name: "Не отправлять отзыв?" });
    await user.click(within(ask).getByRole("button", { name: "Писать дальше" }));
    expect((field(window) as HTMLTextAreaElement).value).toBe("Черновик");

    await user.keyboard("{Escape}");
    await user.click(
      within(await screen.findByRole("alertdialog")).getByRole("button", { name: "Не отправлять" }),
    );
    await closed();

    act(() => openFeedback());
    window = screen.getByRole("dialog", { name: "Написать отзыв" });
    await user.click(within(window).getByRole("button", { name: "Отмена" }));
    await closed();
  });

  it("opens from «О программе»", async () => {
    useMockServer("ok");
    const user = userEvent.setup();
    const Wrapper = withProviders();
    render(
      <Wrapper>
        <AboutSection />
        <OverlayHost />
      </Wrapper>,
    );

    expect(screen.getByText("Нашли ошибку или есть идея?")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Написать отзыв…" }));

    expect(screen.getByRole("dialog", { name: "Написать отзыв" })).toBeTruthy();
  });
});
