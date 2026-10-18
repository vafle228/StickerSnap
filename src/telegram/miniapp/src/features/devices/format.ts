import type { Device } from "../../api/types";
import { plural } from "../../shared/plural";

export const OS: Record<Device["os"], string> = {
  windows: "Windows",
  macos: "macOS",
  linux: "Linux",
};

const DAY_MONTH = new Intl.DateTimeFormat("ru-RU", { day: "numeric", month: "long" });

/** «Windows · с 12 октября» */
export function deviceLabel(device: Pick<Device, "os" | "linkedAt">): string {
  return `${OS[device.os]} · с ${DAY_MONTH.format(new Date(device.linkedAt))}`;
}

/** Under «Устройства» on «Мои паки»: «2 компьютера привязано» */
export function devicesLabel(count: number): string {
  if (count === 0) return "Компьютеров пока нет";
  const computers = plural(count, ["компьютер", "компьютера", "компьютеров"]);
  return `${count} ${computers} ${count % 10 === 1 && count % 100 !== 11 ? "привязан" : "привязано"}`;
}
