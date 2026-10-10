import type { ReactNode } from "react";
import { create } from "zustand";

/* Windows and confirmations of artboard «12 · Окна и попапы»: one window (layer 2) and one
   confirmation (layer 3) at a time. The host renders them; Esc, the focus trap and closing the
   open popover come from Dialog and the layer stack (ds/layers.ts). Popovers (layer 1) are
   anchored to their button and live in <Popover>. */

export type CloseOverlay = () => void;
export type RenderOverlay = (close: CloseOverlay) => ReactNode;

interface Slot {
  id: number;
  render: RenderOverlay;
  onClose?: () => void;
}

interface OverlayState {
  window: Slot | null;
  confirm: Slot | null;
}

export const useOverlays = create<OverlayState>()(() => ({ window: null, confirm: null }));

let nextId = 1;

type SlotName = keyof OverlayState;

/** Closes the slot if it still shows overlay `id`; a stale close of a replaced overlay does nothing. */
export function closeSlot(slot: SlotName, id: number) {
  const current = useOverlays.getState()[slot];
  if (current?.id !== id) return;
  useOverlays.setState({ [slot]: null });
  current.onClose?.();
}

function open(slot: SlotName, render: RenderOverlay, onClose?: () => void): CloseOverlay {
  const id = nextId++;
  // A newer window replaces the open one: the old one closes as if by «Отмена».
  const previous = useOverlays.getState()[slot];
  useOverlays.setState({ [slot]: { id, render, onClose } });
  previous?.onClose?.();
  return () => closeSlot(slot, id);
}

export const overlays = {
  /** Shows a window (layer 2): `render` gets `close` for its Dialog's onClose and buttons. */
  openWindow(render: RenderOverlay, onClose?: () => void): CloseOverlay {
    return open("window", render, onClose);
  },
  /** Shows a confirmation (layer 3) above the window. Screens call confirm() from notify instead. */
  openConfirm(render: RenderOverlay, onClose?: () => void): CloseOverlay {
    return open("confirm", render, onClose);
  },
  /** Closes everything, e.g. when the section changes. */
  closeAll() {
    const { window, confirm } = useOverlays.getState();
    useOverlays.setState({ window: null, confirm: null });
    confirm?.onClose?.();
    window?.onClose?.();
  },
};
