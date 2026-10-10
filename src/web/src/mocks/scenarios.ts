/** The dev switch of the demo data (contract, «Как контракт попадает в код»). */
export const SCENARIOS = {
  ok: "Всё в порядке",
  "source-missing": "Исходник не найден",
  "pack-full": "Пак заполнен",
  offline: "Нет сети",
  "long-video": "Длинное видео",
  "short-gif": "Короткий GIF",
  "no-segments": "Без отрезков",
  desktop: "Десктоп",
} as const;

export type Scenario = keyof typeof SCENARIOS;

export function isScenario(value: string | null): value is Scenario {
  return value !== null && value in SCENARIOS;
}
