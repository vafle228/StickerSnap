import { useState } from "react";
import { mediaSrc } from "../../shared/api/client";
import { placeholder } from "./format";

interface StickerMediaProps {
  /** mediaUrl of a sticker, or a pack's coverUrl; none draws the placeholder */
  src?: string | undefined;
  /** Picks the placeholder's tint and shape, so neighbours differ */
  index: number;
  /** Plays in a loop, as Telegram shows it; otherwise stays on its first frame */
  playing?: boolean;
  /** Of the placeholder */
  shapeClassName: string | undefined;
}

/** A sticker's .webm as the server keeps it from Telegram. Until it loads, or when it cannot
 *  (no network the first time, demo data), the tinted shape of the artboard stands in. */
export function StickerMedia({ src, index, playing = false, shapeClassName }: StickerMediaProps) {
  const [failed, setFailed] = useState<string>();
  const art = placeholder(index);

  if (!src || failed === src) {
    return (
      <span
        className={shapeClassName}
        style={{ background: art.tint, borderRadius: art.radius }}
        aria-hidden
      />
    );
  }
  return (
    <video
      key={src}
      src={mediaSrc(src)}
      muted
      loop={playing}
      autoPlay={playing}
      playsInline
      preload="metadata"
      aria-hidden
      onError={() => setFailed(src)}
    />
  );
}
