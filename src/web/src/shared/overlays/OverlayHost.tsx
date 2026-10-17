import { useState } from "react";
import { DialogPresence } from "@stickersnap/ds/layers";
import { closeSlot, useOverlays, type Slot } from "./overlays";

type SlotName = "window" | "confirm";

interface Shown {
  slot: SlotName;
  overlay: Slot;
  open: boolean;
}

/** Mounted once at the root: the open window and the confirmation above it.
 *  Each Dialog portals itself to <body>; toasts live in NotifyHost, above both.
 *  A closed or replaced overlay stays rendered, closed, until its Dialog has faded out. */
export function OverlayHost() {
  const window = useOverlays((s) => s.window);
  const confirm = useOverlays((s) => s.confirm);
  const [shown, setShown] = useState<Shown[]>([]);

  // Derived during render: the current overlays open, the ones that went away closing.
  const current = [
    window && { slot: "window" as const, overlay: window },
    confirm && { slot: "confirm" as const, overlay: confirm },
  ].filter((s) => s !== null);
  const next: Shown[] = shown.map((s) => ({
    ...s,
    open: current.some((c) => c.overlay.id === s.overlay.id),
  }));
  for (const c of current) {
    if (!next.some((s) => s.overlay.id === c.overlay.id)) next.push({ ...c, open: true });
  }
  if (
    next.length !== shown.length ||
    next.some((s, i) => s.open !== shown[i]?.open || s.overlay !== shown[i]?.overlay)
  ) {
    setShown(next);
  }

  const exited = (id: number) => setShown((list) => list.filter((s) => s.overlay.id !== id));

  // Windows first, confirmations above them, each in the order it opened.
  const ordered = [...next].sort((a, b) =>
    a.slot === b.slot ? a.overlay.id - b.overlay.id : a.slot === "window" ? -1 : 1,
  );
  return (
    <>
      {ordered.map(({ slot, overlay, open }) => (
        <DialogPresence.Provider
          key={overlay.id}
          value={{ open, onExited: open ? undefined : () => exited(overlay.id) }}
        >
          {overlay.render(() => closeSlot(slot, overlay.id))}
        </DialogPresence.Provider>
      ))}
    </>
  );
}
