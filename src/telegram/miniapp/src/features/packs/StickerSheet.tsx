import { useEffect, useRef, useState, type KeyboardEvent, type TouchEvent } from "react";
import { Button, Icon, IconButton } from "@stickersnap/ds";
import type { Sticker } from "../../api/types";
import { Sheet } from "../../shared/Sheet";
import styles from "./pack.module.css";

/** How far a finger moves sideways to page to the neighbour */
const SWIPE_PX = 50;

/** The sticker up close (artboard 13, section 4): animated, its emoji and number, and the one
 *  action, «Удалить из пака…». Swipes and the arrows page through the pack. */
export function StickerSheet({
  stickers,
  index,
  onIndex,
  onClose,
  onDelete,
}: {
  stickers: Sticker[];
  index: number;
  onIndex: (index: number) => void;
  onClose: () => void;
  onDelete: (sticker: Sticker) => void;
}) {
  const sticker = stickers[index];
  const start = useRef<number | null>(null);
  if (!sticker) return null;

  const go = (to: number) => {
    if (to >= 0 && to < stickers.length && to !== index) onIndex(to);
  };
  const onTouchStart = (e: TouchEvent) => {
    start.current = e.touches[0]?.clientX ?? null;
  };
  const onTouchEnd = (e: TouchEvent) => {
    const x = e.changedTouches[0]?.clientX;
    const from = start.current;
    start.current = null;
    if (x === undefined || from === null) return;
    if (x - from > SWIPE_PX) go(index - 1);
    else if (from - x > SWIPE_PX) go(index + 1);
  };
  const onKeyDown = (e: KeyboardEvent) => {
    if (e.key === "ArrowLeft") go(index - 1);
    else if (e.key === "ArrowRight") go(index + 1);
  };

  return (
    <Sheet label="Просмотр стикера" onClose={onClose}>
      <div onKeyDown={onKeyDown} className={styles.sheetBody}>
        <div className={styles.sheetHead}>
          <h2 className={styles.sheetTitle}>
            Стикер {index + 1} из {stickers.length}
          </h2>
          <IconButton icon="close" label="Закрыть" flat size="sm" onClick={onClose} />
        </div>
        {/* A new sticker gets a fresh player: the still state belongs to one video */}
        <Preview
          key={sticker.id}
          sticker={sticker}
          onTouchStart={onTouchStart}
          onTouchEnd={onTouchEnd}
          prev={index > 0 ? () => go(index - 1) : undefined}
          next={index < stickers.length - 1 ? () => go(index + 1) : undefined}
          onDelete={() => onDelete(sticker)}
        />
      </div>
    </Sheet>
  );
}

function Preview({
  sticker,
  onTouchStart,
  onTouchEnd,
  prev,
  next,
  onDelete,
}: {
  sticker: Sticker;
  onTouchStart: (e: TouchEvent) => void;
  onTouchEnd: (e: TouchEvent) => void;
  prev?: () => void;
  next?: () => void;
  onDelete: () => void;
}) {
  const video = useRef<HTMLVideoElement>(null);
  // Transparent VP9 does not play in every Telegram webview (iPhone): the first frame then
  // stays, with a line saying so (artboard 14, state 13)
  const [still, setStill] = useState(false);

  useEffect(() => {
    const player = video.current;
    if (!player) return;
    player.play()?.catch(() => setStill(true));
  }, []);

  return (
    <>
      <div
        className={`${styles.preview} ${still ? styles.short : ""}`}
        onTouchStart={onTouchStart}
        onTouchEnd={onTouchEnd}
      >
        <IconButton
          icon="chevron-left"
          label="Предыдущий стикер"
          flat
          size="sm"
          className={`${styles.nav} ${styles.prev}`}
          disabled={!prev}
          onClick={prev}
        />
        {still ? (
          <img className={styles.media} src={sticker.thumbUrl} alt={sticker.emoji.join(" ")} />
        ) : (
          <video
            ref={video}
            className={styles.media}
            src={sticker.videoUrl}
            poster={sticker.thumbUrl}
            aria-label={sticker.emoji.join(" ")}
            autoPlay
            loop
            muted
            playsInline
            onError={() => setStill(true)}
          />
        )}
        <IconButton
          icon="chevron-right"
          label="Следующий стикер"
          flat
          size="sm"
          className={`${styles.nav} ${styles.next}`}
          disabled={!next}
          onClick={next}
        />
      </div>
      <div className={styles.stickerEmoji}>
        <span>{sticker.emoji.join(" ")}</span>
        {!still && <small>По этим эмодзи Telegram подсказывает стикер</small>}
      </div>
      {still && (
        <p className={styles.stillNote}>
          <Icon name="info" size={16} />
          <span>Анимация здесь не играет, виден первый кадр. В самом Telegram стикер живой.</span>
        </p>
      )}
      <Button icon="trash" className={styles.danger} onClick={onDelete}>
        Удалить из пака…
      </Button>
    </>
  );
}
