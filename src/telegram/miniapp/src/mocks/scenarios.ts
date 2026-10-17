/** The dev switch of the demo data: one scenario per state of artboard 14 that needs the server. */
export const SCENARIOS = {
  ok: "Всё в порядке",
  slow: "Медленная сеть",
  "no-packs": "Без паков",
  "no-devices": "Без компьютеров",
  "pack-missing": "Пак пропал",
  "delete-fails": "Не удаляется",
  "no-video": "Без анимации",
  offline: "Нет связи",
  unauthorized: "Не вошли",
} as const;

export type Scenario = keyof typeof SCENARIOS;

export function isScenario(value: string | null): value is Scenario {
  return value !== null && value in SCENARIOS;
}
