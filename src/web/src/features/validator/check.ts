import { createElement } from "react";
import { api, call } from "../../shared/api/client";
import type { SourceRef, ValidationReport } from "../../shared/api/types";
import { notifyProblem } from "../../shared/notify";
import { overlays } from "../../shared/overlays";
import { platform, type LocalFile } from "../../shared/platform";
import { readFile } from "../projects/reading";
import { ValidationDialog } from "./ValidationDialog";

/** The checklist of a finished .webm (R8) in a window. `makeSticker` offers to start a project
 *  from the same file: a .webm dropped on the window may be a video, not a sticker. */
export function showReport(report: ValidationReport, makeSticker?: () => void) {
  overlays.openWindow((close) =>
    createElement(ValidationDialog, {
      report,
      onClose: close,
      onMakeSticker: makeSticker
        ? () => {
            close();
            makeSticker();
          }
        : undefined,
    }),
  );
}

/** A .webm dropped on the window: the file goes to the server once, is checked by every rule
 *  of the Telegram table, and the checklist opens. Checking is quick, so it reads like an import. */
export async function checkWebm(file: LocalFile, makeSticker: (source: SourceRef) => void) {
  let source: SourceRef | undefined;
  const result = await readFile(file, "webm", async (signal) => {
    source = await platform.sourceOf(file, signal);
    return call(api.POST("/api/v1/validate", { body: { source }, signal }));
  });
  if (!result) return;
  if ("error" in result) {
    notifyProblem(result.error, { key: "validate" });
    return;
  }
  const uploaded = source;
  showReport(result.value, uploaded && (() => makeSticker(uploaded)));
}
