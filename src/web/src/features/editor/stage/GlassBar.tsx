import { Button, IconButton } from "../../../ds";
import { t } from "../../../shared/strings/ru";
import { num } from "../format";
import { useEditor } from "../model/store";
import { ZOOM_MAX, ZOOM_MIN, zoomIn, zoomOut } from "../model/zoom";
import styles from "./VideoStage.module.css";

/** The glass bar over the video (artboard 3a): the frame tool, zoom and the exact preview.
 *  «Рамка» opens the whole source with the frame's handles; «Готово» goes back to the sticker
 *  as it comes out (the edits themselves land as they are made).
 *  «Точное превью» is a draft render, part of the import/export task; its place is kept. */
export function GlassBar({ scale }: { scale: number }) {
  const zoom = useEditor((s) => s.zoom);
  const setZoom = useEditor((s) => s.setZoom);
  const cropTool = useEditor((s) => s.cropTool);
  const setCropTool = useEditor((s) => s.setCropTool);
  const zoomValue = zoom === "fit" ? t("editor.zoomFit") : `${num(zoom * 100)}%`;

  return (
    <div
      className={`ss-card ss-card-glass ${styles.glassBar}`}
      role="toolbar"
      aria-label={t("editor.tools")}
    >
      <Button
        size="sm"
        variant="ghost"
        icon={cropTool ? "check" : "crop"}
        className={styles.toggle}
        aria-pressed={cropTool}
        onClick={() => setCropTool(!cropTool)}
      >
        {t(cropTool ? "editor.cropDone" : "editor.crop")}
      </Button>
      <span className={styles.divider} aria-hidden />
      <IconButton
        icon="minus"
        label={t("editor.zoomOut")}
        size="sm"
        flat
        className={styles.round}
        disabled={scale <= ZOOM_MIN + 1e-6}
        onClick={() => setZoom(zoomOut(scale))}
      />
      <button
        type="button"
        className={styles.zoomValue}
        aria-label={t("editor.zoomReset", { value: zoomValue })}
        disabled={zoom === "fit"}
        onClick={() => setZoom("fit")}
      >
        {zoomValue}
      </button>
      <IconButton
        icon="plus"
        label={t("editor.zoomIn")}
        size="sm"
        flat
        className={styles.round}
        disabled={scale >= ZOOM_MAX - 1e-6}
        onClick={() => setZoom(zoomIn(scale))}
      />
      <span className={styles.divider} aria-hidden />
      <Button size="sm" variant="ghost" icon="clock" className={styles.ink} disabled>
        {t("editor.exactPreview")}
      </Button>
    </div>
  );
}
