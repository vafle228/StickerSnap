import type { ReactNode } from "react";
import { Button } from "./Button";
import { Dialog } from "./Dialog";
import type { IconName } from "./Icon";

export interface ConfirmDialogProps {
  open?: boolean;
  /** A question with the verb: «Удалить стикер из пака?» */
  title: ReactNode;
  /** What is lost: «Он пропадёт у всех, кто добавил пак.» */
  text?: ReactNode;
  /** The same verb: «Удалить» */
  confirmLabel: string;
  cancelLabel?: string;
  icon?: IconName;
  onConfirm: () => void;
  onCancel: () => void;
  inline?: boolean;
}

/** Irreversible actions only. Focus starts on «Отмена», so Enter deletes nothing;
 *  Esc means «Отмена», the scrim does not close it (artboard 12). */
export function ConfirmDialog({
  open,
  title,
  text,
  confirmLabel,
  cancelLabel = "Отмена",
  icon,
  onConfirm,
  onCancel,
  inline,
}: ConfirmDialogProps) {
  return (
    <Dialog
      open={open}
      inline={inline}
      alert
      title={title}
      text={text}
      onClose={onCancel}
      actions={
        <>
          <Button variant="secondary" data-autofocus onClick={onCancel}>
            {cancelLabel}
          </Button>
          <Button variant="danger" icon={icon} onClick={onConfirm}>
            {confirmLabel}
          </Button>
        </>
      }
    />
  );
}
