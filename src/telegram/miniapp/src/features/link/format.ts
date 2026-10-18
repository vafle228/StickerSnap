import type { LinkRequest } from "../../api/types";
import { plural } from "../../shared/plural";
import { OS } from "../devices/format";

/** «Система»: what the desktop says about itself («Windows 11»), else the family */
export function systemLabel(link: Pick<LinkRequest, "os" | "platform">): string {
  return link.platform?.trim() || OS[link.os];
}

/** «Запрос»: how long ago «Привязать» was pressed on the computer, «2 минуты назад» */
export function requestedLabel(requestedAt: string, now = Date.now()): string {
  const minutes = Math.floor((now - Date.parse(requestedAt)) / 60_000);
  if (minutes < 1) return "только что";
  return `${minutes} ${plural(minutes, ["минуту", "минуты", "минут"])} назад`;
}
