// @vitest-environment happy-dom
import { launch, server } from "../../test/dom";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { useNav } from "../../app/nav";
import { useToast } from "../../shared/toast";

const question = () => screen.findByRole("heading", { name: "Привязать этот компьютер?" });
const button = (name: string) => screen.getByRole("button", { name });

describe("«Привязать этот компьютер?»", () => {
  it("opens from the desktop's link with what the desktop told about itself", async () => {
    const { telegram } = launch({ startParam: "link_demo" });
    await question();
    const list = within(screen.getByLabelText("Запрос на привязку"));
    expect(list.getByText("DESKTOP-7F3K2Q")).toBeTruthy();
    expect(list.getByText("Windows 11")).toBeTruthy();
    expect(list.getByText("StickerSnap 0.3.1")).toBeTruthy();
    expect(list.getByText("2 минуты назад")).toBeTruthy();
    expect(screen.getByRole("switch", { name: /Уведомления в чат/ })).toHaveProperty(
      "checked",
      true,
    );
    // Alone on the stack: Telegram's header says «Закрыть»
    expect(telegram.calls.backButton).toBe(false);
  });

  it("links the computer and says so, with the switch as it was left", async () => {
    const { db, telegram } = launch({ startParam: "link_demo" });
    await question();
    await userEvent.click(screen.getByRole("switch", { name: /Уведомления в чат/ }));
    await userEvent.click(button("Привязать"));
    expect(await screen.findByRole("heading", { name: "Компьютер привязан" })).toBeTruthy();
    expect(screen.getByText(/Вернитесь в StickerSnap на DESKTOP-7F3K2Q/)).toBeTruthy();
    expect(db.links.find((l) => l.code === "demo")).toMatchObject({
      answer: "confirmed",
      notifications: false,
    });
    expect(telegram.calls.haptics).toEqual(["done"]);
    expect(telegram.calls.writeAccessAsked).toBe(0);

    await userEvent.click(button("К моим пакам"));
    expect(useNav.getState().stack).toEqual([{ kind: "packs" }]);
  });

  it("asks Telegram for write access first when the bot may not write yet", async () => {
    const { db, telegram } = launch({ startParam: "link_demo", canWrite: false });
    await question();
    await userEvent.click(button("Привязать"));
    await screen.findByRole("heading", { name: "Компьютер привязан" });
    expect(telegram.calls.writeAccessAsked).toBe(1);
    expect(db.links.find((l) => l.code === "demo")?.answer).toBe("confirmed");
  });

  it("does not link without write access, and «Разрешить» asks again", async () => {
    const { db, telegram } = launch({
      startParam: "link_demo",
      canWrite: false,
      writeAnswers: [false, true],
    });
    await question();
    await userEvent.click(button("Привязать"));
    await waitFor(() => expect(useToast.getState().current?.title).toBe("Компьютер не привязан"));
    expect(db.links.find((l) => l.code === "demo")?.answer).toBeUndefined();
    expect(telegram.calls.haptics).toEqual(["error"]);
    expect(screen.getByRole("heading", { name: "Привязать этот компьютер?" })).toBeTruthy();

    await userEvent.click(button("Разрешить"));
    await screen.findByRole("heading", { name: "Компьютер привязан" });
    expect(telegram.calls.writeAccessAsked).toBe(2);
  });

  it("cancels on «Это не я» without asking", async () => {
    const { db } = launch({ startParam: "link_demo" });
    await question();
    await userEvent.click(button("Это не я"));
    expect(await screen.findByRole("heading", { name: "Привязка отменена" })).toBeTruthy();
    expect(db.links.find((l) => l.code === "demo")?.answer).toBe("declined");
  });

  it("says the link is out of date for a code that is gone", async () => {
    launch({ startParam: "link_old" });
    expect(await screen.findByRole("heading", { name: "Ссылка устарела" })).toBeTruthy();
  });

  it("says the link is out of date when the code runs out while the screen is open", async () => {
    const { db } = launch({ startParam: "link_demo" });
    await question();
    db.links = [];
    await userEvent.click(button("Привязать"));
    expect(await screen.findByRole("heading", { name: "Ссылка устарела" })).toBeTruthy();
  });

  it("links a computer that is this person's already without asking", async () => {
    const { db } = launch({ startParam: "link_again" });
    expect(await screen.findByRole("heading", { name: "Компьютер привязан" })).toBeTruthy();
    const again = db.links.find((l) => l.code === "again");
    expect(again?.answer).toBe("confirmed");
    // Without the switch: notifications muted before stay muted
    expect(again && "notifications" in again && again.notifications !== undefined).toBe(false);
  });

  it("keeps the question when the service does not answer, and «Повторить» tries again", async () => {
    const { db } = launch({ startParam: "link_demo" });
    await question();
    server.use(
      http.post("*/v1/miniapp/links/:code/confirm", () => HttpResponse.error(), { once: true }),
    );
    await userEvent.click(button("Привязать"));
    await waitFor(() => expect(useToast.getState().current?.title).toBe("Компьютер не привязан"));
    expect(screen.getByRole("heading", { name: "Привязать этот компьютер?" })).toBeTruthy();

    await userEvent.click(button("Повторить"));
    await screen.findByRole("heading", { name: "Компьютер привязан" });
    expect(db.links.find((l) => l.code === "demo")?.answer).toBe("confirmed");
  });

  it("offers «Повторить» when the request cannot be loaded", async () => {
    launch({ startParam: "link_demo", scenario: "offline" });
    expect(await screen.findByText("Не удалось загрузить запрос")).toBeTruthy();
  });
});
