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
  /** The main button drawn inside the zone («Выбрать видео…»); the whole zone is that button */
  action?: string;
  /** A quiet line under the button («Готовый стикер .webm тоже можно бросить сюда…») */
  note?: ReactNode;
  /** A file is being read: the zone takes no clicks or drops */
  busy?: boolean;
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
  action,
  note,
  busy,
  dragging,
  onFiles,
  onClick,
  className,
}: DropZoneProps) {
  const [over, setOver] = useState(false);
  const gradientId = useId();
  const active = !busy && (dragging ?? over);

  const drop = (e: DragEvent<HTMLButtonElement>) => {
    e.preventDefault();
    setOver(false);
    if (!busy) onFiles?.(Array.from(e.dataTransfer.files));
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
      // Not `disabled`: a disabled button drops its drag events and the browser opens the file.
      aria-disabled={busy || undefined}
      aria-busy={busy || undefined}
      onClick={busy ? undefined : onClick}
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
      {action && (
        <span className="ss-btn ss-btn-primary ss-drop-action" aria-hidden>
          {action}
        </span>
      )}
      {note && <p className="ss-drop-note">{note}</p>}
    </button>
  );
}
