import { Banner, Button, Collapse } from "../../../ds";
import { t } from "../../../shared/strings/ru";
import { useProjectActions } from "../../projects/actions";
import { useEditor } from "../model/store";

/** Artboard 6-2: the project opened without its video. The banner stays above the canvas until
 *  a video is found; segments and settings are intact, export waits for the source.
 *  It slides in only when the video goes missing during work: a project opened without it
 *  shows the banner already in place. */
export function MissingSource() {
  const projectId = useEditor((s) => s.projectId);
  const source = useEditor((s) => s.source);
  const { replaceSource } = useProjectActions();
  const missing = projectId !== undefined && source?.status === "missing";

  return (
    <Collapse key={projectId} open={missing}>
      <Banner
        tone="warning"
        title={t("editor.missing.title", { fileName: source?.fileName ?? "" })}
        description={t("editor.missing.text")}
        action={
          <Button
            size="sm"
            onClick={() => {
              if (projectId) void replaceSource(projectId);
            }}
          >
            {t("editor.missing.pick")}
          </Button>
        }
      />
    </Collapse>
  );
}
