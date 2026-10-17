import { useId } from "react";
import { Button, TextField } from "@stickersnap/ds";
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

/** «Папки», desktop app only: where new projects and rendered .webm go. A new folder is for
 *  new files only. «Изменить…» opens the system folder picker, «Открыть» the file manager. */
export function FoldersSection() {
  const folders = useSettings().data?.folders;
  const { mutate } = useUpdateSettings();

  const change = async (folder: Folder, path: string) => {
    const picked = await platform.pickFolder(path);
    if (picked && picked !== path) mutate({ folders: { [folder]: picked } });
  };

  return (
    <SettingsSection id="folders" title={t("settings.folders")} text={t("settings.folders.text")}>
      {FOLDERS.map(({ folder, label, note }) => (
        <FolderRow
          key={folder}
          label={t(label)}
          note={t(note)}
          path={folders?.[folder] ?? ""}
          onPick={() => void change(folder, folders?.[folder] ?? "")}
        />
      ))}
    </SettingsSection>
  );
}

function FolderRow({
  label,
  note,
  path,
  onPick,
}: {
  label: string;
  note: string;
  path: string;
  onPick: () => void;
}) {
  const labelId = useId();
  return (
    <div className={styles.group}>
      <span className={styles.label} id={labelId}>
        {label}
      </span>
      <div className={styles.folderLine}>
        <TextField
          className={styles.path}
          aria-labelledby={labelId}
          readOnly
          title={path}
          value={path}
          hint={note}
        />
        <Button onClick={onPick}>{t("settings.folders.change")}</Button>
        <Button variant="ghost" onClick={() => platform.openFolder(path)}>
          {t("settings.folders.open")}
        </Button>
      </div>
    </div>
  );
}
