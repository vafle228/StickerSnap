import type { ButtonHTMLAttributes, ReactNode } from "react";
import { cx } from "../cx";
import { StickerArt } from "./StickerArt";

export interface StickerTileProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  src?: string;
  art?: ReactNode;
  name: string;
  /** e.g. "2,4 с" */
  duration?: string;
  /** Format or a warning, bottom-left, two at most */
  badges?: ReactNode[];
  selected?: boolean;
  /** Telegram emoji of the sticker; three shown, the rest as «+N» */
  emoji?: string | string[];
  /** Source project name, shown when the tile stands in a pack */
  project?: string;
}

/** Sticker preview, always on the transparency checker. */
export function StickerTile({
  src,
  art,
  name,
  duration,
  badges,
  selected = false,
  emoji,
  project,
  className,
  ...rest
}: StickerTileProps) {
  const list = emoji === undefined ? [] : [emoji].flat();
  return (
    <button
      type="button"
      aria-pressed={selected}
      {...rest}
      className={cx("ss-sticker", selected && "is-selected", className)}
    >
      <div className="ss-checker">
        {art ?? (src ? <img src={src} alt="" /> : <StickerArt />)}
        {list.length > 0 && (
          <span className="ss-sticker-emoji" aria-label={`Эмодзи: ${list.join(" ")}`}>
            {list.slice(0, 3).join("")}
            {list.length > 3 && ` +${list.length - 3}`}
          </span>
        )}
        {badges && badges.length > 0 && (
          <div className="ss-sticker-badges">{badges.slice(0, 2)}</div>
        )}
      </div>
      <div className="ss-sticker-meta">
        <span className="ss-sticker-name">{name}</span>
        {duration && <span className="ss-sticker-time">{duration}</span>}
      </div>
      {project && <div className="ss-sticker-src">из «{project}»</div>}
    </button>
  );
}
