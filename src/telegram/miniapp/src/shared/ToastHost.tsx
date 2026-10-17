import { Button, Toast } from "@stickersnap/ds";
import { useToast } from "./toast";
import styles from "./ToastHost.module.css";

/** The one popup, full width at the bottom, above the phone's gesture bar. */
export function ToastHost() {
  const current = useToast((s) => s.current);
  const close = useToast((s) => s.close);
  if (!current) return null;
  const { id, tone, title, description, action } = current;
  return (
    <div className={`ss-root ${styles.host}`}>
      <Toast
        key={id}
        tone={tone}
        title={title}
        description={description}
        onClose={() => close(id)}
        action={
          action && (
            <Button
              size="sm"
              onClick={() => {
                close(id);
                action.run();
              }}
            >
              {action.label}
            </Button>
          )
        }
      />
    </div>
  );
}
