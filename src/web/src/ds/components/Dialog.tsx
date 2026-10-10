import { useEffect, useId, useRef, type KeyboardEvent, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { cx } from "../cx";
import { useLayer } from "../layers";
import { IconButton } from "./IconButton";

export interface DialogProps {
  open?: boolean;
  title: ReactNode;
  text?: ReactNode;
  children?: ReactNode;
  /** Buttons, right-aligned: secondary first, the main action last */
  actions?: ReactNode;
  onClose?: () => void;
  /** 640px: the publish window */
  wide?: boolean;
  /** alertdialog: no close cross; Esc still means onClose («Отмена») */
  alert?: boolean;
  /** Click on the scrim closes it. Off by default: a window holds a choice.
   *  Only read-only windows («Лицензии…») turn it on (artboard 12). */
  dismissible?: boolean;
  /** Render in place (previews, canvas) instead of a fixed overlay */
  inline?: boolean;
  className?: string;
}

const FOCUSABLE =
  "a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex='-1'])";

/** Window (layer 2) or confirmation (layer 3, `alert`). Holds Tab inside, returns focus to the opener.
 *  An element marked data-autofocus gets the focus first («Отмена» in ConfirmDialog). */
export function Dialog({
  open = true,
  title,
  text,
  children,
  actions,
  onClose,
  wide,
  alert,
  dismissible = false,
  inline,
  className,
}: DialogProps) {
  const titleId = useId();
  const ref = useRef<HTMLDivElement>(null);
  const live = open && !inline;

  useLayer(live, alert ? "confirm" : "dialog", () => onClose?.());

  useEffect(() => {
    if (!live) return undefined;
    const opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const box = ref.current;
    (box?.querySelector<HTMLElement>("[data-autofocus]") ?? box)?.focus();
    return () => opener?.focus();
  }, [live]);

  if (!open) return null;

  const holdTab = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.key !== "Tab" || inline) return;
    const list = Array.from(e.currentTarget.querySelectorAll<HTMLElement>(FOCUSABLE));
    const first = list[0];
    const last = list[list.length - 1];
    if (!first || !last) return;
    if (
      e.shiftKey &&
      (document.activeElement === first || document.activeElement === ref.current)
    ) {
      e.preventDefault();
      last.focus();
    } else if (!e.shiftKey && document.activeElement === last) {
      e.preventDefault();
      first.focus();
    }
  };

  const overlay = (
    <div
      className={cx("ss-root ss-overlay", inline && "ss-overlay-inline")}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget && dismissible && !alert) onClose?.();
      }}
    >
      <div
        ref={ref}
        tabIndex={-1}
        role={alert ? "alertdialog" : "dialog"}
        aria-modal={inline ? undefined : true}
        aria-labelledby={titleId}
        aria-describedby={text ? `${titleId}-text` : undefined}
        className={cx("ss-dialog", wide && "ss-dialog-wide", className)}
        onKeyDown={holdTab}
      >
        <div className="ss-dialog-head">
          <h2 className="ss-dialog-title" id={titleId}>
            {title}
          </h2>
          {onClose && !alert && (
            <IconButton icon="close" label="Закрыть" flat size="sm" onClick={onClose} />
          )}
        </div>
        {text && (
          <p className="ss-dialog-text" id={`${titleId}-text`}>
            {text}
          </p>
        )}
        {children && <div className="ss-dialog-content">{children}</div>}
        {actions && <div className="ss-dialog-actions">{actions}</div>}
      </div>
    </div>
  );

  return inline ? overlay : createPortal(overlay, document.body);
}
