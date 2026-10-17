import { createElement } from "react";
import { EXIT_FALLBACK_MS } from "@stickersnap/ds/hooks";
import { create } from "zustand";
import { ConfirmDialog, type IconName, type Tone } from "@stickersnap/ds";
import { overlays } from "../overlays/overlays";
import { t } from "../strings/ru";
import { DetailsDialog, type ToastDetails } from "./DetailsDialog";

/* Popups of artboard «8 · Оповещения». Banners and marks are not here: a screen draws them
   from its own state. Lifetimes (6 s, 10 s with an action, warnings and errors until closed)
   live in the DS Toast; the cap of three lives here, so a pushed-out toast is gone for good. */

export interface ToastAction {
  label: string;
  onClick: () => void;
}

export interface ToastOptions {
  tone?: Tone;
  /** What happened */
  title: string;
  /** A mark beside the title: «с уступками» on a finished export (artboard 8) */
  badge?: { tone: Tone; label: string };
  /** What to do */
  description?: string;
  icon?: IconName;
  /** One small button; pressing it closes the toast */
  action?: ToastAction;
  /** Errors: the log and traceId behind «Подробности» */
  details?: ToastDetails;
  /** 0–100: a running background task. update() turns it into the result in place */
  progress?: number;
  progressLabel?: string;
  progressVariant?: "default" | "rainbow";
  /** ms, null = until closed; the default comes from the tone */
  timeout?: number | null;
  /** One toast per key: another with the same key replaces it in place («Не сохранено» once) */
  key?: string;
  /** The user closed it, or it timed out: a running task then reports its result anew */
  onClose?: () => void;
}

export interface ToastEntry extends ToastOptions {
  id: number;
  /** Closed and on its way out: still drawn while it leaves, no longer counted or found by key */
  leaving?: boolean;
}

export interface ToastHandle {
  id: number;
  update: (patch: Partial<ToastOptions>) => void;
  close: () => void;
}

const MAX = 3;

const sticky = (t: ToastOptions) =>
  t.tone === "warning" || t.tone === "danger" || t.progress !== undefined;

/** Drops the entry once its exit has run. NotifyHost calls it when the toast has left;
 *  the timer is the fallback when nothing draws the stack. */
function remove(id: number) {
  useToasts.setState((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) }));
}

function leave(toast: ToastEntry): ToastEntry {
  if (toast.leaving) return toast;
  setTimeout(() => remove(toast.id), EXIT_FALLBACK_MS);
  return { ...toast, leaving: true };
}

/** At most three: a fourth pushes out the oldest info or success toast. Leaving ones don't count. */
function capped(toasts: ToastEntry[]): ToastEntry[] {
  const list = [...toasts];
  const shown = () => list.filter((t) => !t.leaving).length;
  while (shown() > MAX) {
    const oldest = list.findIndex((t) => !t.leaving && !sticky(t));
    if (oldest < 0) break;
    const entry = list[oldest];
    if (!entry) break;
    list[oldest] = leave(entry);
  }
  return list;
}

export const useToasts = create<{ toasts: ToastEntry[] }>()(() => ({ toasts: [] }));

let nextId = 1;

function close(id: number) {
  useToasts.setState((s) => ({ toasts: s.toasts.map((t) => (t.id === id ? leave(t) : t)) }));
}

function update(id: number, patch: Partial<ToastOptions>) {
  useToasts.setState((s) => ({
    toasts: capped(s.toasts.map((t) => (t.id === id ? { ...t, ...patch } : t))),
  }));
}

function handle(id: number): ToastHandle {
  return { id, update: (patch) => update(id, patch), close: () => close(id) };
}

export const notify = {
  /** Shows a popup and returns a handle to update or close it. */
  toast(options: ToastOptions): ToastHandle {
    const same = options.key
      ? useToasts.getState().toasts.find((t) => t.key === options.key && !t.leaving)
      : undefined;
    if (same) {
      useToasts.setState((s) => ({
        toasts: s.toasts.map((t) => (t.id === same.id ? { ...options, id: same.id } : t)),
      }));
      return handle(same.id);
    }
    const id = nextId++;
    useToasts.setState((s) => ({ toasts: capped([...s.toasts, { ...options, id }]) }));
    return handle(id);
  },

  close,

  /** The toast has left the screen (NotifyHost). */
  exited: remove,

  /** «Подробности» of an error: a read-only window, so a click on the scrim closes it. */
  showDetails(details: ToastDetails) {
    overlays.openWindow((closeWindow) =>
      createElement(DetailsDialog, { details, onClose: closeWindow }),
    );
  },
};

export interface ConfirmOptions {
  /** A question with the verb: «Удалить стикер из пака?» */
  title: string;
  /** What is lost */
  text?: string;
  /** The same verb: «Удалить» */
  confirmLabel: string;
  cancelLabel?: string;
  icon?: IconName;
}

/** Asks before an irreversible action (layer 3). Resolves true on the verb, false on «Отмена»,
 *  Esc, or when another confirmation replaces it. */
export function confirm(options: ConfirmOptions): Promise<boolean> {
  return new Promise((resolve) => {
    let answered = false;
    const answer = (yes: boolean) => {
      if (answered) return;
      answered = true;
      resolve(yes);
    };
    overlays.openConfirm(
      (closeConfirm) =>
        createElement(ConfirmDialog, {
          title: options.title,
          text: options.text,
          confirmLabel: options.confirmLabel,
          cancelLabel: options.cancelLabel ?? t("common.cancel"),
          icon: options.icon,
          onConfirm: () => {
            answer(true);
            closeConfirm();
          },
          onCancel: closeConfirm,
        }),
      () => answer(false),
    );
  });
}
