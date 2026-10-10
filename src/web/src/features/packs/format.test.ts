import { describe, expect, it } from "vitest";
import type { PackSummary } from "../../shared/api/types";
import { countdown, packBadge, packMeta, sendLabel } from "./format";

const pack = (patch: Partial<PackSummary> = {}): PackSummary => ({
  name: "reactions_call_by_StickerSnapAppBot",
  title: "Реакции с созвона",
  type: "regular",
  count: 12,
  limit: 120,
  status: "ok",
  ...patch,
});

describe("packMeta", () => {
  it("says how full a pack is", () => {
    expect(packMeta(pack())).toEqual({ text: "12 из 120", tone: "normal" });
  });

  it("puts a missing pack before anything else", () => {
    const gone = pack({ status: "missing", sending: { jobId: "j", done: 1, total: 2 } });
    expect(packMeta(gone)).toEqual({ text: "Не найден в Telegram", tone: "warning" });
  });

  it("shows a send in progress", () => {
    expect(packMeta(pack({ sending: { jobId: "j", done: 2, total: 4 } }))).toEqual({
      text: "Отправляем… 2 из 4",
      tone: "busy",
    });
  });

  it("warns about a full pack", () => {
    expect(packMeta(pack({ count: 120 }))).toEqual({
      text: "Заполнен · 120 из 120",
      tone: "warning",
    });
  });
});

describe("packBadge", () => {
  it("names the kind of pack", () => {
    expect(packBadge(pack())).toBe("Стикеры · 12 из 120");
    expect(packBadge(pack({ type: "custom_emoji", count: 18, limit: 200 }))).toBe(
      "Эмодзи · 18 из 200",
    );
  });
});

describe("sendLabel", () => {
  it("agrees the noun with the count", () => {
    expect(sendLabel(1)).toBe("Опубликовать 1 стикер");
    expect(sendLabel(3)).toBe("Опубликовать 3 стикера");
    expect(sendLabel(5)).toBe("Опубликовать 5 стикеров");
    expect(sendLabel(0)).toBe("Опубликовать");
  });
});

describe("countdown", () => {
  it("rounds up to whole seconds", () => {
    expect(countdown(23_200)).toBe("0:24");
    expect(countdown(61_000)).toBe("1:01");
    expect(countdown(-5)).toBe("0:00");
  });
});
