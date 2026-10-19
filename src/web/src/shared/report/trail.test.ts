// @vitest-environment happy-dom
import { beforeEach, describe, expect, it } from "vitest";
import { describeClick, describeRequest, STEP_MAX, trail, TRAIL_LIMIT } from "./trail";

describe("the trail of the last steps", () => {
  beforeEach(() => trail.clear());

  it("keeps the last 30, the oldest first", () => {
    for (let i = 0; i < TRAIL_LIMIT + 5; i++) trail.record(`Кнопка: ${i}`);

    const steps = trail.list();
    expect(steps).toHaveLength(TRAIL_LIMIT);
    expect(steps[0]?.what).toBe("Кнопка: 5");
    expect(steps.at(-1)?.what).toBe("Кнопка: 34");
  });

  it("counts a step repeated right after itself instead of filling the trail", () => {
    trail.record("GET /api/v1/jobs → 200", new Date("2026-10-19T13:00:00Z"));
    trail.record("GET /api/v1/jobs → 200", new Date("2026-10-19T13:00:01Z"));
    trail.record("GET /api/v1/jobs → 200", new Date("2026-10-19T13:00:02Z"));

    expect(trail.list()).toEqual([
      { at: "2026-10-19T13:00:02.000Z", what: "GET /api/v1/jobs → 200 (×3)" },
    ]);
  });

  it("cuts a long step and skips an empty one", () => {
    trail.record(`Кнопка: ${"я".repeat(500)}`);
    trail.record("   ");

    expect(trail.list()).toHaveLength(1);
    expect(trail.list()[0]?.what).toHaveLength(STEP_MAX);
  });
});

describe("what a click is called", () => {
  const html = (markup: string) => {
    document.body.innerHTML = markup;
    return document.body;
  };

  it("names a button by its label or its words", () => {
    const body = html(
      `<button aria-label="Сообщить об ошибке"><svg></svg>Сообщить</button><button><span>Экспорт</span></button>`,
    );
    const [report, exp] = Array.from(body.querySelectorAll("button"));

    expect(describeClick(report?.querySelector("svg") ?? null)).toBe("Кнопка: Сообщить об ошибке");
    expect(describeClick(exp?.querySelector("span") ?? null)).toBe("Кнопка: Экспорт");
  });

  it("names a menu item and a checkbox by their words, never by what a field holds", () => {
    const body = html(
      `<div role="menuitem">Удалить</div><label><input type="checkbox" />Можно ответить мне</label><input type="text" value="секрет" />`,
    );

    expect(describeClick(body.querySelector("[role=menuitem]"))).toBe("Меню: Удалить");
    expect(describeClick(body.querySelector("input[type=checkbox]"))).toBe(
      "Переключатель: Можно ответить мне",
    );
    expect(describeClick(body.querySelector("input[type=text]"))).toBeUndefined();
    expect(describeClick(body)).toBeUndefined();
  });

  it("names a request by its method, path and status, without its query", () => {
    expect(
      describeRequest("post", "http://127.0.0.1:5180/api/v1/packs/name-check?name=cats", 409),
    ).toBe("POST /api/v1/packs/name-check → 409");
  });
});
