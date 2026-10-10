import { useState, type KeyboardEvent, type MouseEvent, type ReactNode } from "react";
import { cx } from "../cx";
import { Badge } from "./Badge";
import { Icon } from "./Icon";
import { IconButton } from "./IconButton";
import { StickerArt } from "./StickerArt";

export interface ProjectCardProps {
  name: string;
  /** «3 стикера · вчера»; «Исходник не найден» when the source is missing */
  meta?: string;
  duration?: string;
  /** The cover image; the placeholder art stays until a real picture loads */
  cover?: string;
  art?: ReactNode;
  /** Source video not found */
  missing?: boolean;
  /** The fix of a missing source, drawn on the cover in place of the flag («Указать исходник») */
  sourceAction?: { label: string; title?: string; onClick: () => void };
  /** Replaces the name while it is being renamed (an inline field) */
  nameEditor?: ReactNode;
  onOpen?: () => void;
  /** Opens the «⋯» Menu at the button */
  onMenu?: (e: MouseEvent<HTMLButtonElement>) => void;
  menuOpen?: boolean;
  menuLabel?: string;
  openLabel?: string;
  /** Keys on the focused card, e.g. F2 to rename */
  onKeyDown?: (e: KeyboardEvent<HTMLElement>) => void;
  className?: string;
}

/** Project in the «Проекты» grid: 4:3 cover on the checker, name, meta, «⋯». */
export function ProjectCard({
  name,
  meta,
  duration,
  cover,
  art,
  missing,
  sourceAction,
  nameEditor,
  onOpen,
  onMenu,
  menuOpen,
  menuLabel = "Действия с проектом",
  openLabel,
  onKeyDown,
  className,
}: ProjectCardProps) {
  // A 1×1 or broken cover counts as none: the placeholder art stays.
  const [shown, setShown] = useState(false);

  return (
    <article className={cx("ss-project", missing && "is-missing", className)} onKeyDown={onKeyDown}>
      <button
        type="button"
        className="ss-project-open"
        aria-label={openLabel ?? `Открыть «${name}»`}
        onClick={onOpen}
      />
      <div className="ss-checker">
        {art ?? (
          <>
            {!shown && <StickerArt />}
            {cover && (
              <img
                src={cover}
                alt=""
                hidden={!shown}
                onLoad={(e) => setShown(e.currentTarget.naturalWidth > 1)}
                onError={() => setShown(false)}
              />
            )}
          </>
        )}
        {missing && !sourceAction && (
          <span className="ss-project-flag">
            <Badge tone="warning" icon="alert">
              Исходник не найден
            </Badge>
          </span>
        )}
        {missing && sourceAction && (
          <button
            type="button"
            className="ss-btn ss-btn-sm ss-project-source"
            title={sourceAction.title}
            onClick={sourceAction.onClick}
          >
            <Icon name="link" size={16} />
            {sourceAction.label}
          </button>
        )}
        {duration && <span className="ss-project-dur">{duration}</span>}
      </div>
      <div className="ss-project-foot">
        <div className="ss-project-text">
          {nameEditor ?? <p className="ss-project-name">{name}</p>}
          {meta && <p className="ss-project-meta">{meta}</p>}
        </div>
        {onMenu && (
          <IconButton
            icon="more"
            label={menuLabel}
            flat
            size="sm"
            aria-haspopup="menu"
            aria-expanded={menuOpen ?? false}
            className={cx(menuOpen && "is-open")}
            onClick={onMenu}
          />
        )}
      </div>
    </article>
  );
}
