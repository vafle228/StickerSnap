import { Button, IconButton } from "../../../ds";
import { t } from "../../../shared/strings/ru";
import { num } from "../format";
import { useEditor, useSegment } from "../model/store";
import { ZOOM_MAX, ZOOM_MIN, zoomIn, zoomOut } from "../model/zoom";
import { useRender } from "../render/store";
import styles from "./VideoStage.module.css";

/** The glass bar over the video (artboard 3a): the frame tool, zoom and the exact preview.
 *  «Рамка» opens the whole source with the frame's handles; «Готово» goes back to the sticker
 *  as it comes out (the edits themselves land as they are made).
 *  «Точное превью» stays pressed: every settled edit renders a draft by the export's own
 *  pipeline (3c, D). During «Сравнить» the bar leads with the way back. */
export function GlassBar({ scale, comparing }: { scale: number; comparing: boolean }) {
  const zoom = useEditor((s) => s.zoom);
  const setZoom = useEditor((s) => s.setZoom);
  const cropTool = useEditor((s) => s.cropTool);
  const setCropTool = useEditor((s) => s.setCropTool);
  const zoomValue = zoom === "fit" ? t("editor.zoomFit") : `${num(zoom * 100)}%`;
  const segment = useSegment();
  const missing = useEditor((s) => s.source?.status === "missing");
  const exact = useRender((s) => s.exact);
  const setExact = useRender((s) => s.setExact);
  const setCompare = useRender((s) => s.setCompare);

  return (
    <div
      className={`ss-card ss-card-glass ${styles.glassBar}`}
      role="toolbar"
      aria-label={t("editor.tools")}
    >
      {comparing && (
        <>
          <Button
            size="sm"
            variant="ghost"
            icon="close"
            className={styles.ink}
            onClick={() => setCompare(false)}
          >
            {t("editor.compare.done")}
          </Button>
          <span className={styles.divider} aria-hidden />
        </>
      )}
      <Button
        size="sm"
        variant="ghost"
        icon={cropTool ? "check" : "crop"}
        className={styles.toggle}
        aria-pressed={cropTool}
        onClick={() => {
          setCompare(false);
          setCropTool(!cropTool);
        }}
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
      <Button
        size="sm"
        variant="ghost"
        icon="clock"
        className={styles.toggle}
        aria-pressed={exact}
        disabled={!segment || missing}
        onClick={() => {
          setCompare(false);
          setExact(!exact);
        }}
      >
        {t("editor.exactPreview")}
      </Button>
    </div>
  );
}
