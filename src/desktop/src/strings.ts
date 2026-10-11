/* The few words the shell says itself; everything else is the SPA's (strings/ru.ts). */
export const ru = {
  appName: "StickerSnap",
  serverFailedTitle: "StickerSnap не запустился",
  serverFailed: "Не удалось запустить локальный сервер приложения.",
  serverStoppedTitle: "StickerSnap остановился",
  serverStopped: "Локальный сервер приложения неожиданно завершился. Перезапустите StickerSnap.",
  logHint: (path: string) => `Подробности в журнале: ${path}`,
  filterVideo: "Видео и GIF",
  filterWebm: "Стикер .webm",
  filterProject: "Проект StickerSnap",
  filterAll: "Все файлы",
  openVideo: "Импорт видео",
  openWebm: "Проверка .webm",
  openProject: "Открыть проект",
  pickFolder: "Выбор папки",
  saveFile: "Сохранить стикер",
} as const;
