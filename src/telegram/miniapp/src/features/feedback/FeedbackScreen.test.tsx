// @vitest-environment happy-dom
import { launch, server } from "../../test/dom";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { useNav } from "../../app/nav";
import { useToast } from "../../shared/toast";

const form = () => screen.findByRole("heading", { name: "Отзыв" });
const button = (name: string) => screen.getByRole("button", { name });
const text = () => screen.getByRole("textbox");

describe("«Отзыв»", () => {
  it("opens from the card on «Мои паки»", async () => {
    const { telegram } = launch();
    await userEvent.click(await screen.findByRole("button", { name: /Написать отзыв/ }));
    await form();
    expect(useNav.getState().stack).toEqual([{ kind: "packs" }, { kind: "feedback" }]);
    expect(telegram.calls.backButton).toBe(true);
  });

  it("opens at once from the bot's /feedback, a bug by default with replies allowed", async () => {
    launch({ startParam: "feedback" });
    await form();
    expect(screen.getByRole("radio", { name: "Ошибка", checked: true })).toBeTruthy();
    expect(screen.getByLabelText("Что случилось")).toBe(text());
    expect(screen.getByText("0 / 2000")).toBeTruthy();
    expect(screen.getByRole("switch", { name: /Можно написать мне/ })).toHaveProperty(
      "checked",
      true,
    );
  });

  it("names the field by the type", async () => {
    launch({ startParam: "feedback" });
    await form();
    await userEvent.click(screen.getByRole("radio", { name: "Идея" }));
    expect(screen.getByLabelText("Что предложить")).toBe(text());
    await userEvent.click(screen.getByRole("radio", { name: "Другое" }));
    expect(screen.getByLabelText("Что хотите сказать")).toBe(text());
  });

  it("sends the type, the text and the switch, then thanks alone on the stack", async () => {
    const { db, telegram } = launch({ startParam: "feedback", platform: "android" });
    await form();
    await userEvent.click(screen.getByRole("radio", { name: "Идея" }));
    await userEvent.type(text(), "  Тёмная тема для превью  ");
    expect(screen.getByText("26 / 2000")).toBeTruthy();
    await userEvent.click(screen.getByRole("switch", { name: /Можно написать мне/ }));
    await userEvent.click(button("Отправить"));

    expect(await screen.findByRole("heading", { name: "Спасибо, отзыв у нас" })).toBeTruthy();
    expect(db.feedback).toEqual([
      { type: "idea", text: "Тёмная тема для превью", canReply: false, platform: "android" },
    ]);
    expect(telegram.calls.haptics).toEqual(["done"]);
    // Telegram's header says «Закрыть», and closing no longer asks
    expect(useNav.getState().stack).toEqual([{ kind: "feedbackSent" }]);
    expect(telegram.calls.backButton).toBe(false);
    expect(telegram.calls.closingConfirmation).toBe(false);

    await userEvent.click(button("К моим пакам"));
    expect(useNav.getState().stack).toEqual([{ kind: "packs" }]);
  });

  it("does not send an empty text and says why, the button staying on", async () => {
    const { db, telegram } = launch({ startParam: "feedback" });
    await form();
    await userEvent.type(text(), "   ");
    await userEvent.click(button("Отправить"));
    expect(screen.getByText("Напишите хотя бы пару слов")).toBeTruthy();
    expect(text().getAttribute("aria-invalid")).toBe("true");
    expect(button("Отправить")).toHaveProperty("disabled", false);
    expect(telegram.calls.haptics).toEqual(["error"]);
    expect(db.feedback).toEqual([]);

    await userEvent.type(text(), "Пак пустой");
    expect(screen.queryByText("Напишите хотя бы пару слов")).toBeNull();
  });

  it("asks «Выйти без отправки?» on «Назад» while there is text", async () => {
    const { telegram } = launch({ startParam: "feedback" });
    await form();
    await userEvent.type(text(), "Пак пустой");
    expect(telegram.calls.closingConfirmation).toBe(true);

    telegram.pressBack();
    expect(await screen.findByRole("alertdialog", { name: "Выйти без отправки?" })).toBeTruthy();
    await userEvent.click(button("Отмена"));
    expect(useNav.getState().stack).toHaveLength(2);
    expect(text()).toHaveProperty("value", "Пак пустой");

    telegram.pressBack();
    await userEvent.click(await screen.findByRole("button", { name: "Выйти" }));
    expect(useNav.getState().stack).toEqual([{ kind: "packs" }]);
    expect(telegram.calls.closingConfirmation).toBe(false);
  });

  it("goes back without asking when there is nothing to lose", async () => {
    const { telegram } = launch({ startParam: "feedback" });
    await form();
    telegram.pressBack();
    expect(useNav.getState().stack).toEqual([{ kind: "packs" }]);
  });

  it("keeps the text when the service is out of reach, and «Повторить» sends it", async () => {
    const { db } = launch({ startParam: "feedback" });
    await form();
    await userEvent.type(text(), "Пак пустой");
    server.use(http.post("*/v1/feedback", () => HttpResponse.error(), { once: true }));
    await userEvent.click(button("Отправить"));

    await waitFor(() => expect(useToast.getState().current?.title).toBe("Не отправилось"));
    expect(useToast.getState().current?.description).toBe(
      "Нет связи с сервисом. Текст остался в форме.",
    );
    expect(text()).toHaveProperty("value", "Пак пустой");

    useToast.getState().current?.action?.run();
    expect(await screen.findByRole("heading", { name: "Спасибо, отзыв у нас" })).toBeTruthy();
    expect(db.feedback).toHaveLength(1);
  });

  it("says when the hour's limit is spent, with nothing to retry", async () => {
    launch({ startParam: "feedback", scenario: "feedback-limit" });
    await form();
    await userEvent.type(text(), "Шестой");
    await userEvent.click(button("Отправить"));
    await waitFor(() =>
      expect(useToast.getState().current?.description).toBe(
        "Слишком много отзывов подряд, попробуйте через час.",
      ),
    );
    expect(useToast.getState().current?.action).toBeUndefined();
    expect(text()).toHaveProperty("value", "Шестой");
  });
});
