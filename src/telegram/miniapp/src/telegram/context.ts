import { createContext, useContext } from "react";
import type { Telegram } from "./bridge";

export const TelegramContext = createContext<Telegram | null>(null);

export function useTelegram(): Telegram {
  const telegram = useContext(TelegramContext);
  if (!telegram) throw new Error("useTelegram outside <Providers>");
  return telegram;
}
