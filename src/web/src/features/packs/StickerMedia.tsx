import { useState } from "react";
import { mediaSrc } from "../../shared/api/client";
import type { PackSticker } from "../../shared/api/types";
import { placeholder } from "./format";

interface StickerMediaProps {
  /** mediaUrl of a sticker, or a pack's coverUrl; none draws the placeholder */
  src?: string | undefined;
  /** What the file is; a cover does not say, so a video that will not play is tried as a picture */
  format?: PackSticker["format"] | undefined;
  /** Picks the placeholder's tint and shape, so neighbours differ */
  index: number;
  /** Plays in a loop, as Telegram shows it; otherwise stays on its first frame */
  playing?: boolean;
  /** Of the placeholder */
  shapeClassName: string | undefined;
}

/** A sticker's file as the server keeps it from Telegram: a .webm, or in a pack copied from
 *  @Stickers a .webp picture. Until it loads, or when it cannot (no network the first time, demo
 *  data, a .tgs the app does not draw), the tinted shape of the artboard stands in. */
export function StickerMedia({
  src,
  format,
  index,
  playing = false,
  shapeClassName,
}: StickerMediaProps) {
  // What failed for which src: a video falls back to a picture, a picture to the placeholder
  const [failed, setFailed] = useState<{ src: string; as: "video" | "picture" }>();
  const art = placeholder(index);
  const failedHere = failed && failed.src === src ? failed.as : undefined;

  if (!src || format === "animated" || failedHere === "picture") {
    return (
      <span
        className={shapeClassName}
        style={{ background: art.tint, borderRadius: art.radius }}
        aria-hidden
      />
    );
  }
  if (format === "static" || failedHere === "video") {
    return (
      <img
        key={src}
        src={mediaSrc(src)}
        alt=""
        aria-hidden
        onError={() => setFailed({ src, as: "picture" })}
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
      onError={() => setFailed({ src, as: "video" })}
    />
  );
}
