// Generated from src/contract/openapi.yaml (x-events) by tools/build-events.mjs. Do not edit.
import type { components } from "./schema";

type Schemas = components["schemas"];

export const HUB_PATH = "/hubs/events";

/** SignalR events of the hub: the server sends a whole snapshot, never a diff. */
export interface HubEvents {
  /** смена состояния и прогресс не чаще 10 раз в секунду. Updates: поп-апы, карточка экспорта, плашка черновика, точка на NavRail */
  jobUpdated: Schemas["Job"];
  /** сохранение, новая обложка, исчез или нашёлся исходник. Updates: сетка «Проектов», недавние на стартовом экране */
  projectChanged: Schemas["ProjectSummary"];
  /** убран из списка или удалён. Updates: сетка «Проектов», недавние на стартовом экране */
  projectRemoved: Schemas["ProjectRemovedEvent"];
  /** бот получил Start, код истёк, бот заблокирован. Updates: раздел «Публикация в Telegram», плашка 7-2, окно публикации */
  accountChanged: Schemas["AccountLink"];
  /** отправлен стикер, сверка, удаление. Updates: список «Моих паков»; открытый пак перечитывается */
  packChanged: Schemas["PackSummary"];
  /** после PATCH и после публикации (недавние эмодзи). Updates: тема, акцент, «Недавние» в выборе эмодзи */
  settingsChanged: Schemas["Settings"];
  /** смена состояния и прогресс не чаще 10 раз в секунду. Updates: карточка скачивания в зоне импорта, поп-апы «Скачиваю…» и «Видео скачано» */
  downloadUpdated: Schemas["Download"];
}

export type HubEvent = keyof HubEvents;

export const HUB_EVENTS: readonly HubEvent[] = ["jobUpdated", "projectChanged", "projectRemoved", "accountChanged", "packChanged", "settingsChanged", "downloadUpdated"];
