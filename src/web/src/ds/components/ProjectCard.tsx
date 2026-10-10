import type { MouseEvent, ReactNode } from "react";
import { cx } from "../cx";
import { Badge } from "./Badge";
import { IconButton } from "./IconButton";
import { StickerArt } from "./StickerArt";

export interface ProjectCardProps {
  name: string;
  /** «3 стикера · вчера» */
  meta?: string;
  duration?: string;
  cover?: string;
  art?: ReactNode;
  /** Source video not found */
  missing?: boolean;
  onOpen?: () => void;
  /** Opens the «⋯» Menu at the button */
  onMenu?: (e: MouseEvent<HTMLButtonElement>) => void;
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
  onOpen,
  onMenu,
  className,
}: ProjectCardProps) {
  return (
    <article className={cx("ss-project", missing && "is-missing", className)}>
      <button
        type="button"
        className="ss-project-open"
        aria-label={`Открыть «${name}»`}
        onClick={onOpen}
      />
      <div className="ss-checker">
        {art ?? (cover ? <img src={cover} alt="" /> : <StickerArt />)}
        {missing && (
          <span className="ss-project-flag">
            <Badge tone="warning" icon="alert">
              Исходник не найден
            </Badge>
          </span>
        )}
        {duration && <span className="ss-project-dur">{duration}</span>}
      </div>
      <div className="ss-project-foot">
        <div className="ss-project-text">
          <p className="ss-project-name">{name}</p>
          {meta && <p className="ss-project-meta">{meta}</p>}
        </div>
        {onMenu && (
          <IconButton
            icon="more"
            label="Действия с проектом"
            flat
            size="sm"
            aria-haspopup="menu"
            onClick={onMenu}
          />
        )}
      </div>
    </article>
  );
}
