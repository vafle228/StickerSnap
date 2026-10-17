import type { Telegram } from "../telegram/bridge";
import { startStack, useNav } from "./nav";

/** Before the first render: the screens of the launch link, and Telegram told we are ready. */
export function boot(telegram: Telegram) {
  useNav.getState().reset(startStack(telegram.startParam));
  telegram.showSettings();
  telegram.ready();
}
