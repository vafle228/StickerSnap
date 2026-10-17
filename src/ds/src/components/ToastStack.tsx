import { Children, isValidElement, type ReactElement, type ReactNode } from "react";
import { cx } from "../cx";
import { usePresence } from "../hooks";
import type { ToastProps } from "./Toast";

export interface ToastStackProps {
  /** Toasts, oldest first */
  children?: ReactNode;
  /** Render in place (previews, canvas) */
  inline?: boolean;
  className?: string;
}

const MAX = 3;

/** Warnings, errors and running tasks stay until closed, so they are never pushed out. */
function sticky(toast: ReactElement<ToastProps>) {
  const { tone, progress } = toast.props;
  return tone === "warning" || tone === "danger" || progress != null;
}

/** One place in the stack: the toast leaves to the right, then the place closes up. */
function ToastSlot({ toast }: { toast: ReactElement<ToastProps> }) {
  const { mounted, state, ref } = usePresence(!toast.props.leaving, toast.props.onExited);
  if (!mounted) return null;
  return (
    <div ref={ref} className="ss-toast-slot" data-state={state}>
      <div className="ss-toast-clip">{toast}</div>
    </div>
  );
}

/** Bottom-right stack, 24px from the window edges, 380 wide, newest at the bottom.
 *  At most three: a fourth pushes out the oldest info or success toast (artboard 8).
 *  It lies above the scrim of windows and Esc never touches it (artboard 12). */
export function ToastStack({ children, inline, className }: ToastStackProps) {
  let toasts = Children.toArray(children).filter(isValidElement) as ReactElement<ToastProps>[];
  const shown = () => toasts.filter((t) => !t.props.leaving).length;
  while (shown() > MAX) {
    const oldest = toasts.findIndex((t) => !t.props.leaving && !sticky(t));
    if (oldest < 0) break;
    toasts = toasts.filter((_, i) => i !== oldest);
  }
  return (
    <div className={cx("ss-toasts", inline && "ss-toasts-inline", className)} aria-live="polite">
      {toasts.map((toast) => (
        <ToastSlot key={toast.key} toast={toast} />
      ))}
    </div>
  );
}
