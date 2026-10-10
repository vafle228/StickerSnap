import { useId, useState, type DragEvent, type ReactNode } from "react";
import { cx } from "../cx";
import { PRISM_STOPS } from "../prism";
import { Icon, type IconName } from "./Icon";

export interface DropZoneProps {
  title?: ReactNode;
  /** null hides the hint line */
  hint?: ReactNode | null;
  dropTitle?: ReactNode;
  icon?: IconName;
  /** tile: «Новый проект» in the projects grid, 4:3 */
  variant?: "zone" | "tile";
  /** Force the drag-over look (rainbow dashes) */
  dragging?: boolean;
  onFiles?: (files: File[]) => void;
  /** Opens the file picker */
  onClick?: () => void;
  className?: string;
}

/** Import zone. Grey dashes at rest; rainbow dashes only while a file is carried over it. */
export function DropZone({
  title = "Перетащите видео или GIF сюда",
  hint,
  dropTitle = "Отпустите, чтобы открыть",
  icon,
  variant = "zone",
  dragging,
  onFiles,
  onClick,
  className,
}: DropZoneProps) {
  const [over, setOver] = useState(false);
  const gradientId = useId();
  const active = dragging ?? over;

  const drop = (e: DragEvent<HTMLButtonElement>) => {
    e.preventDefault();
    setOver(false);
    onFiles?.(Array.from(e.dataTransfer.files));
  };

  return (
    <button
      type="button"
      className={cx(
        "ss-drop",
        active && "is-dragging",
        variant === "tile" && "ss-drop-tile",
        className,
      )}
      onClick={onClick}
      onDragEnter={(e) => {
        e.preventDefault();
        setOver(true);
      }}
      onDragOver={(e) => e.preventDefault()}
      onDragLeave={(e) => {
        if (!(e.relatedTarget instanceof Node && e.currentTarget.contains(e.relatedTarget))) {
          setOver(false);
        }
      }}
      onDrop={drop}
    >
      <svg className="ss-drop-line" aria-hidden>
        <defs>
          <linearGradient id={gradientId} x1="0" y1="0" x2="1" y2="1">
            {PRISM_STOPS.map((color, i) => (
              <stop key={color} offset={i / 3} stopColor={color} />
            ))}
          </linearGradient>
        </defs>
        <rect rx={20} ry={20} style={active ? { stroke: `url(#${gradientId})` } : undefined} />
      </svg>
      <span className="ss-drop-icon" aria-hidden>
        <Icon name={icon ?? (variant === "tile" ? "plus" : "upload")} size={22} />
      </span>
      <p className="ss-drop-title">{active ? dropTitle : title}</p>
      {hint !== null && <p className="ss-drop-hint">{hint ?? "MP4, MOV, WEBM или GIF"}</p>}
    </button>
  );
}
