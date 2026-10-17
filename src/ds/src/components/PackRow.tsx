import type { MouseEvent, ReactNode } from "react";
import { cx } from "../cx";
import { Badge } from "./Badge";
import { IconButton } from "./IconButton";
import { StickerArt } from "./StickerArt";

export interface PackRowProps {
  title: ReactNode;
  /** «Видеостикеры» */
  meta?: ReactNode;
  count?: number;
  /** Telegram limit, 120 for video stickers */
  limit?: number;
  cover?: string;
  art?: ReactNode;
  /** The pack no longer exists in Telegram */
  missing?: boolean;
  /** Extra badges, e.g. «отправляем…» while sending */
  badges?: ReactNode;
  onMenu?: (e: MouseEvent<HTMLButtonElement>) => void;
  className?: string;
}

/** Pack row inside a Card on «Мои паки». */
export function PackRow({
  title,
  meta = "Видеостикеры",
  count,
  limit = 120,
  cover,
  art,
  missing,
  badges,
  onMenu,
  className,
}: PackRowProps) {
  const full = count !== undefined && count >= limit;
  return (
    <div className={cx("ss-pack", className)} role="listitem">
      <div className="ss-checker">
        {art ?? (cover ? <img src={cover} alt="" /> : <StickerArt />)}
      </div>
      <div className="ss-pack-text">
        <p className="ss-pack-name">{title}</p>
        <p className="ss-pack-meta">{meta}</p>
      </div>
      <div className="ss-pack-badges">
        {missing && (
          <Badge tone="warning" icon="alert">
            Не найден
          </Badge>
        )}
        {full && (
          <Badge tone="neutral" icon="check">
            Заполнен
          </Badge>
        )}
        {badges}
      </div>
      {count !== undefined && (
        <span className="ss-pack-count">
          {count} из {limit}
        </span>
      )}
      {onMenu && (
        <IconButton
          icon="more"
          label="Действия с паком"
          flat
          size="sm"
          aria-haspopup="menu"
          onClick={onMenu}
        />
      )}
    </div>
  );
}
