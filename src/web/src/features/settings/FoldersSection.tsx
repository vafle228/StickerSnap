import { useEffect, useId, useRef, useState } from "react";
import { Button, TextField } from "../../ds";
import { useSettings, useUpdateSettings } from "../../shared/api/queries";
import type { Settings } from "../../shared/api/types";
import { platform } from "../../shared/platform";
import { t, type StringKey } from "../../shared/strings/ru";
import { SettingsSection } from "./Section";
import styles from "./sections.module.css";

type Folder = keyof Settings["folders"];

const FOLDERS: readonly { folder: Folder; label: StringKey; note: StringKey }[] = [
  { folder: "projects", label: "settings.folders.projects", note: "settings.folders.projectsNote" },
  { folder: "stickers", label: "settings.folders.stickers", note: "settings.folders.stickersNote" },
];

/** «Папки»: where new projects and rendered .webm go. A new folder is for new files only. */
export function FoldersSection() {
  const folders = useSettings().data?.folders;
  const { mutate } = useUpdateSettings();

  return (
    <SettingsSection id="folders" title={t("settings.folders")} text={t("settings.folders.text")}>
      {FOLDERS.map(({ folder, label, note }) => (
        <FolderRow
          key={folder}
          label={t(label)}
          note={t(note)}
          path={folders?.[folder] ?? ""}
          onChange={(path) => mutate({ folders: { [folder]: path } })}
        />
      ))}
    </SettingsSection>
  );
}

/** With paths (Electron) «Изменить…» opens the system folder picker. The browser has none, so
 *  the field itself turns editable: Enter or leaving it saves, Esc cancels. */
function FolderRow({
  label,
  note,
  path,
  onChange,
}: {
  label: string;
  note: string;
  path: string;
  onChange: (path: string) => void;
}) {
  const labelId = useId();
  const [draft, setDraft] = useState<string | null>(null);
  const editing = draft !== null;
  const box = useRef<HTMLDivElement>(null);

  // The field already exists, so autoFocus would not fire: focus it when editing starts.
  useEffect(() => {
    if (!editing) return;
    const input = box.current?.querySelector("input");
    input?.focus();
    input?.select();
  }, [editing]);

  const commit = () => {
    const next = draft?.trim();
    setDraft(null);
    if (next && next !== path) onChange(next);
  };

  const change = async () => {
    if (!platform.hasPaths) {
      setDraft(path);
      return;
    }
    const picked = await platform.pickFolder(path);
    if (picked && picked !== path) onChange(picked);
  };

  return (
    <div className={styles.group}>
      <span className={styles.label} id={labelId}>
        {label}
      </span>
      <div className={styles.folderLine}>
        <div ref={box} className={styles.path}>
          <TextField
            aria-labelledby={labelId}
            title={editing ? undefined : path}
            readOnly={!editing}
            spellCheck={false}
            value={draft ?? path}
            hint={editing ? t("settings.folders.typeHint") : note}
            onChange={(e) => setDraft(e.target.value)}
            onBlur={() => editing && commit()}
            onKeyDown={(e) => {
              if (!editing) return;
              if (e.key === "Enter") commit();
              if (e.key === "Escape") {
                e.stopPropagation();
                setDraft(null);
              }
            }}
          />
        </div>
        {editing ? (
          <Button
            // mousedown keeps the focus in the field, so blur does not save before the click
            onMouseDown={(e) => e.preventDefault()}
            onClick={commit}
          >
            {t("settings.folders.done")}
          </Button>
        ) : (
          <Button onClick={() => void change()}>{t("settings.folders.change")}</Button>
        )}
        {platform.hasPaths && !editing && (
          <Button variant="ghost" onClick={() => platform.openFolder(path)}>
            {t("settings.folders.open")}
          </Button>
        )}
      </div>
    </div>
  );
}
