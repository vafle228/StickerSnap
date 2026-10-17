import { create } from "zustand";
import type { Tone } from "@stickersnap/ds";

/** A popup at the bottom of the Mini App (artboard 14, «Поп-ап»): one at a time, a new one
 *  replaces the old. Lifetimes are the design system's (artboard 8). */
export interface ToastMessage {
  id: number;
  tone: Tone;
  title: string;
  description?: string;
  action?: { label: string; run: () => void };
}

interface Toasts {
  current?: ToastMessage;
  show(message: Omit<ToastMessage, "id">): void;
  close(id: number): void;
}

let next = 0;

export const useToast = create<Toasts>()((set) => ({
  current: undefined,
  show: (message) => set({ current: { ...message, id: ++next } }),
  close: (id) => set(({ current }) => (current?.id === id ? { current: undefined } : { current })),
}));
