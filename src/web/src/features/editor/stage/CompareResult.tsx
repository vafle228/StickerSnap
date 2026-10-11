import { useEffect, useRef } from "react";
import type { ExportResult, Segment } from "../../../shared/api/types";
import { t } from "../../../shared/strings/ru";
import { kilobytes } from "../format";
import { useEditor } from "../model/store";
import { useFileUrl } from "../render/useFileUrl";
import styles from "./VideoStage.module.css";

/** «Сравнить с исходником» (3c, H): the exported file beside the source, the same size. It
 *  starts over whenever the quick preview's loop does, so both run in step. */
export function CompareResult({
  result,
  segment,
  fps,
  size,
}: {
  result: ExportResult;
  segment: Segment;
  fps: number;
  size: { width: number; height: number };
}) {
  const ref = useRef<HTMLVideoElement>(null);
  const url = useFileUrl(result.fileUrl);
  const start = segment.inFrame / fps;

  useEffect(() => {
    const video = ref.current;
    if (!video || !url) return undefined;
    const sync = (playing: boolean) => {
      if (playing) video.play().catch(() => {});
      else video.pause();
    };
    const { playing } = useEditor.getState();
    video.currentTime = 0;
    sync(playing);
    return useEditor.subscribe((s, prev) => {
      if (s.playing !== prev.playing) sync(s.playing);
      // The quick preview went back to the segment's start: the result does too.
      if (s.time <= start + 1e-6 && prev.time > start + 1e-6) video.currentTime = 0;
    });
  }, [url, start]);

  return (
    <div className={`${styles.media} ${styles.result}`} style={size}>
      {url && <video ref={ref} className={styles.exact} src={url} loop muted playsInline />}
      <span className={`ss-card-glass ${styles.paneLabel}`}>
        {t("editor.compare.result", { size: kilobytes(result.bytes) })}
      </span>
    </div>
  );
}
