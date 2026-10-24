/** The dev switch of the demo data (contract, «Как контракт попадает в код»). */
export const SCENARIOS = {
  ok: "Всё в порядке",
  "no-projects": "Без проектов",
  "project-errors": "Ошибки проекта",
  "source-missing": "Исходник не найден",
  "no-packs": "Без паков",
  unlinked: "Без аккаунта",
  "link-wait": "Ждём привязку",
  "link-declined": "Привязку отклонили",
  "pack-full": "Пак заполнен",
  "pack-missing": "Пак пропал",
  "publish-errors": "Ошибки отправки",
  offline: "Нет сети",
  "long-video": "Длинное видео",
  "short-gif": "Короткий GIF",
  "no-segments": "Без отрезков",
  desktop: "Десктоп",
  "import-broken": "Битый файл",
  "too-big": "Не влезет",
  "export-fails": "Экспорт не удался",
  "save-fails": "Не сохраняется",
  "webm-bad": "Чужой .webm",
  "feedback-limit": "Лимит отзывов",
  "screen-crash": "Экран упал",
  "last-crash": "Сбой прошлого запуска",
  "link-long": "Ссылка: длинный ролик",
  "link-gone": "Ссылка: видео недоступно",
  "link-broken": "Ссылка: загрузчик сломан",
  "link-disk": "Ссылка: нет места",
} as const;

export type Scenario = keyof typeof SCENARIOS;

export function isScenario(value: string | null): value is Scenario {
  return value !== null && value in SCENARIOS;
}

/** Whether the demo server answers as the desktop app (mode, export paths): in «Десктоп», and in
 *  every scenario inside the desktop shell, where the real server always says desktop. */
export function runsAsDesktop(scenario: Scenario): boolean {
  return scenario === "desktop" || globalThis.stickersnap !== undefined;
}
