import { Banner, Button } from "../../../ds";
import { t } from "../../../shared/strings/ru";
import { useProjectActions } from "../../projects/actions";
import { useEditor } from "../model/store";

/** Artboard 6-2: the project opened without its video. The banner stays above the canvas until
 *  a video is found; segments and settings are intact, export waits for the source. */
export function MissingSource() {
  const projectId = useEditor((s) => s.projectId);
  const source = useEditor((s) => s.source);
  const { replaceSource } = useProjectActions();
  if (!projectId || source?.status !== "missing") return null;

  return (
    <Banner
      tone="warning"
      title={t("editor.missing.title", { fileName: source.fileName })}
      description={t("editor.missing.text")}
      action={
        <Button size="sm" onClick={() => void replaceSource(projectId)}>
          {t("editor.missing.pick")}
        </Button>
      }
    />
  );
}
