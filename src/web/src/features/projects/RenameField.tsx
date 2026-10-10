import { useRef, useState } from "react";
import { TextField } from "../../ds";
import { t } from "../../shared/strings/ru";
import styles from "./ProjectsScreen.module.css";

/** «Переименовать» (F2) in place of the name: Enter or a click away keeps the new name,
 *  Esc keeps the old one. `onDone` gets undefined for Esc. */
export function RenameField({
  name,
  onDone,
}: {
  name: string;
  onDone: (name: string | undefined) => void;
}) {
  const [value, setValue] = useState(name);
  const done = useRef(false);

  const finish = (result: string | undefined) => {
    if (done.current) return;
    done.current = true;
    onDone(result);
  };

  return (
    <TextField
      autoFocus
      onFocus={(e) => e.currentTarget.select()}
      className={styles.rename}
      aria-label={t("projects.renameField")}
      value={value}
      maxLength={120}
      onChange={(e) => setValue(e.target.value)}
      onBlur={() => finish(value)}
      onKeyDown={(e) => {
        if (e.key === "Enter") finish(value);
        if (e.key === "Escape") {
          e.stopPropagation();
          finish(undefined);
        }
      }}
    />
  );
}
