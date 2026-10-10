import { createPortal } from "react-dom";
import { Button, Toast, ToastStack } from "../../ds";
import { t } from "../strings/ru";
import { notify, useToasts } from "./notify";

/** Mounted once at the root. Portaled to <body> so it lies above window scrims (artboard 12). */
export function NotifyHost() {
  const toasts = useToasts((s) => s.toasts);
  return createPortal(
    <ToastStack className="ss-root">
      {toasts.map((toast) => (
        <Toast
          key={toast.id}
          tone={toast.tone}
          title={toast.title}
          description={toast.description}
          icon={toast.icon}
          progress={toast.progress}
          progressLabel={toast.progressLabel}
          progressVariant={toast.progressVariant}
          timeout={toast.timeout}
          onClose={() => notify.close(toast.id)}
          action={
            toast.action && (
              <Button
                size="sm"
                variant="secondary"
                onClick={() => {
                  notify.close(toast.id);
                  toast.action?.onClick();
                }}
              >
                {toast.action.label}
              </Button>
            )
          }
          secondaryAction={
            toast.details && (
              <Button
                size="sm"
                variant="ghost"
                onClick={() => toast.details && notify.showDetails(toast.details)}
              >
                {t("common.details")}
              </Button>
            )
          }
        />
      ))}
    </ToastStack>,
    document.body,
  );
}
