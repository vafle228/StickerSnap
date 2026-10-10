// All interface strings. Tone follows the design system README: «вы», «пак», Russian numbers.
export const ru = {
  "nav.label": "Разделы приложения",
  "nav.editor": "Редактор",
  "nav.projects": "Проекты",
  "nav.packs": "Мои паки",
  "nav.settings": "Настройки",
  "theme.label": "Тема",
  "theme.system": "Как в системе",
  "theme.light": "Светлая",
  "theme.dark": "Тёмная",
  "shell.empty": "Раздел появится на следующих этапах.",
} as const;

export type StringKey = keyof typeof ru;

export function t(key: StringKey): string {
  return ru[key];
}
