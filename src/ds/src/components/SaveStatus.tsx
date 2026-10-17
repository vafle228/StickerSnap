import { useRef, useState, type ReactNode } from "react";
import { cx } from "../cx";
import { useClickOutside, usePresence } from "../hooks";
import { useLayer } from "../layers";
import { Badge } from "./Badge";

export type SaveState = "saved" | "saving" | "error";

export interface SaveStatusProps {
  state?: SaveState;
  /** Popover text when state is "error" */
  detail?: ReactNode;
  /** One small Button in the popover («Сохранить в другое место…») */
  action?: ReactNode;
  className?: string;
}

const LABELS: Record<SaveState, string> = {
  saved: "Сохранено",
  saving: "Сохраняю…",
  error: "Не сохранено",
};

// Saved is green as on artboard 3a; saving stays neutral until it lands.
const TONES = { saved: "success", saving: "neutral", error: "danger" } as const;

/** Editor header badge. A broken background save is told once by a toast and kept in view here. */
export function SaveStatus({ state = "saved", detail, action, className }: SaveStatusProps) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLSpanElement>(null);
  const canOpen = state === "error";
  const shown = open && canOpen;
  useLayer(shown, "popover", () => setOpen(false));
  useClickOutside(ref, shown, () => setOpen(false));
  const pop = usePresence(shown);

  return (
    <span className={cx("ss-save", className)} ref={ref} role="status">
      <Badge
        tone={TONES[state]}
        icon={state === "saved" ? "check" : state === "error" ? "error-circle" : undefined}
        busy={state === "saving"}
        onClick={canOpen ? () => setOpen(!open) : undefined}
        aria-haspopup={canOpen ? "dialog" : undefined}
        aria-expanded={canOpen ? shown : undefined}
      >
        <span key={state} className="ss-swap">
          {LABELS[state]}
        </span>
      </Badge>
      {pop.mounted && (
        <div
          ref={pop.ref}
          className="ss-save-pop ss-pop"
          data-state={pop.state}
          role="dialog"
          aria-label="Сохранение"
        >
          <p>
            {detail ??
              "Нет места или нет доступа к папке. Правки держим в памяти и пробуем снова каждые 30 с."}
          </p>
          {action}
        </div>
      )}
    </span>
  );
}
