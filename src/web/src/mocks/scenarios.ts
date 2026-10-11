/** The dev switch of the demo data (contract, «Как контракт попадает в код»). */
export const SCENARIOS = {
  ok: "Всё в порядке",
  "no-projects": "Без проектов",
  "project-errors": "Ошибки проекта",
  "source-missing": "Исходник не найден",
  "pack-full": "Пак заполнен",
  offline: "Нет сети",
  "long-video": "Длинное видео",
  "short-gif": "Короткий GIF",
  "no-segments": "Без отрезков",
  desktop: "Десктоп",
  "import-broken": "Битый файл",
  "too-big": "Не влезет",
  "export-fails": "Экспорт не удался",
  "webm-bad": "Чужой .webm",
} as const;

export type Scenario = keyof typeof SCENARIOS;

export function isScenario(value: string | null): value is Scenario {
  return value !== null && value in SCENARIOS;
}
