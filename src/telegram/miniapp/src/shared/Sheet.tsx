import { useEffect, useRef, type ReactNode, type TouchEvent } from "react";
import { useLayer } from "@stickersnap/ds/layers";
import { useTelegram } from "../telegram/context";
import styles from "./Sheet.module.css";

/** How far a finger pulls the sheet down before it closes */
const PULL_PX = 80;

/** A sheet from the bottom over the screen (artboard 14, «Лист снизу»): one at a time; «Назад»,
 *  the back gesture, the cross, a swipe down and a tap on the scrim close it. While it is open,
 *  Telegram's own vertical swipes are off, so the swipe closes the sheet and not the Mini App. */
export function Sheet({
  label,
  onClose,
  children,
}: {
  label: string;
  onClose: () => void;
  children: ReactNode;
}) {
  const telegram = useTelegram();
  const ref = useRef<HTMLDivElement>(null);
  const start = useRef<{ x: number; y: number } | null>(null);

  // «Назад» in Telegram's header and Esc close the top layer first (App.tsx)
  useLayer(true, "dialog", onClose);

  useEffect(() => {
    telegram.setVerticalSwipes(false);
    const opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    ref.current?.focus();
    return () => {
      telegram.setVerticalSwipes(true);
      opener?.focus();
    };
  }, [telegram]);

  const onTouchStart = (e: TouchEvent) => {
    const touch = e.touches[0];
    start.current = touch ? { x: touch.clientX, y: touch.clientY } : null;
  };
  const onTouchEnd = (e: TouchEvent) => {
    const touch = e.changedTouches[0];
    const from = start.current;
    start.current = null;
    if (!touch || !from) return;
    const dy = touch.clientY - from.y;
    if (dy > PULL_PX && dy > Math.abs(touch.clientX - from.x)) onClose();
  };

  return (
    <>
      <div className={styles.scrim} aria-hidden onClick={onClose} />
      <div
        ref={ref}
        className={`ss-root ${styles.sheet}`}
        role="dialog"
        aria-modal="true"
        aria-label={label}
        tabIndex={-1}
        onTouchStart={onTouchStart}
        onTouchEnd={onTouchEnd}
      >
        <span className={styles.grab} aria-hidden />
        {children}
      </div>
    </>
  );
}
