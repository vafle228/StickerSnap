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
  /** A link dragged from a browser and dropped here, instead of a file */
  onText?: (text: string) => void;
  /** Opens the file picker */
  onClick?: () => void;
  className?: string;
  /** Controls of their own under the button («или по ссылке» and its field): the zone is then
   *  a group, its upper part the button, and these stay clickable and focusable on their own */
  children?: ReactNode;
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
  onText,
  onClick,
  className,
  children,
}: DropZoneProps) {
  const [over, setOver] = useState(false);
  const gradientId = useId();
  const active = !busy && (dragging ?? over);

  const drop = (e: DragEvent<HTMLElement>) => {
    e.preventDefault();
    setOver(false);
    if (busy) return;
    const files = Array.from(e.dataTransfer.files);
    const text = e.dataTransfer.getData("text/uri-list") || e.dataTransfer.getData("text/plain");
    // A uri-list may hold several lines: the first link is the one dragged
    if (files.length === 0 && text && onText) onText(text.split(/\r?\n/)[0] ?? text);
    else onFiles?.(files);
  };

  const classes = cx(
    "ss-drop",
    active && "is-dragging",
    variant === "tile" && "ss-drop-tile",
    children !== undefined && "ss-drop-group",
    className,
  );
  const drag = {
    onDragEnter: (e: DragEvent<HTMLElement>) => {
      e.preventDefault();
      setOver(true);
    },
    onDragOver: (e: DragEvent<HTMLElement>) => e.preventDefault(),
    onDragLeave: (e: DragEvent<HTMLElement>) => {
      if (!(e.relatedTarget instanceof Node && e.currentTarget.contains(e.relatedTarget))) {
        setOver(false);
      }
    },
    onDrop: drop,
  };
  // Not `disabled`: a disabled button drops its drag events and the browser opens the file.
  const button = {
    type: "button" as const,
    "aria-disabled": busy || undefined,
    "aria-busy": busy || undefined,
    onClick: busy ? undefined : onClick,
  };
  const line = (
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
  );
  const content = (
    <>
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
    </>
  );

  if (children === undefined) {
    return (
      <button className={classes} {...button} {...drag}>
        {line}
        {content}
      </button>
    );
  }
  return (
    <div className={classes} aria-busy={busy || undefined} {...drag}>
      {line}
      <button className="ss-drop-hit" {...button}>
        {content}
      </button>
      {children}
    </div>
  );
}
