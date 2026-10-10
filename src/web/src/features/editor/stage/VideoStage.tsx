import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { Icon } from "../../../ds";
import { mediaSrc } from "../../../shared/api/client";
import { useAppInfo } from "../../../shared/api/queries";
import { t } from "../../../shared/strings/ru";
import { fps as fpsText, num } from "../format";
import { fpsOf } from "../model/frame";
import { useEditor, useSegment } from "../model/store";
import { fitScale } from "../model/zoom";
import { GlassBar } from "./GlassBar";
import { OutputFrame } from "./OutputFrame";
import { usePlayback } from "./usePlayback";
import styles from "./VideoStage.module.css";

/** Room the video leaves around itself for the glass bar on top and the chips at the bottom. */
const INSET = { top: 72, bottom: 64, side: 24 };

/** The canvas on a checkerboard (artboard 3a): the source in a <video>, the output frame over it,
 *  the glass bar and two glass chips. Crop handles (CropFrame) and the exact preview layer go
 *  on top of this. */
export function VideoStage({ className }: { className?: string }) {
  const source = useEditor((s) => s.source);
  const mediaUrl = useEditor((s) => s.mediaUrl);
  const zoom = useEditor((s) => s.zoom);
  const cropTool = useEditor((s) => s.cropTool);
  const segment = useSegment();
  const limits = useAppInfo().data?.limits;

  const viewportRef = useRef<HTMLDivElement>(null);
  const videoRef = useRef<HTMLVideoElement>(null);
  const [box, setBox] = useState({ width: 0, height: 0 });
  const [usable, setUsable] = useState(false);

  useLayoutEffect(() => {
    const viewport = viewportRef.current;
    if (!viewport) return undefined;
    const observer = new ResizeObserver(([entry]) => {
      if (!entry) return;
      const { width, height } = entry.contentRect;
      setBox({ width, height });
    });
    observer.observe(viewport);
    return () => observer.disconnect();
  }, []);

  // A new source starts unplayable until its first frame arrives.
  useEffect(() => setUsable(false), [mediaUrl]);

  const fps = source ? fpsOf(source) : 30;
  usePlayback(
    videoRef,
    usable,
    segment ?? { inFrame: 0, outFrame: 0, speed: 1, loop: "normal" },
    fps,
  );

  if (!source || !segment) return null;

  const fit = fitScale(source, box);
  const scale = zoom === "fit" ? fit : zoom;
  const width = source.width * scale;
  const height = source.height * scale;
  const present = source.status === "ok" && mediaUrl !== undefined;

  return (
    <section className={`${styles.stage} ${className ?? ""}`} aria-label={t("editor.canvas")}>
      <div
        ref={viewportRef}
        className={styles.viewport}
        style={{ padding: `${INSET.top}px ${INSET.side}px ${INSET.bottom}px` }}
      >
        <div className={styles.media} style={{ width, height }}>
          {!usable && (
            <span className={styles.placeholder}>
              {t("editor.source", { width: source.width, height: source.height })}
            </span>
          )}
          {present && (
            <video
              ref={videoRef}
              className={styles.video}
              src={mediaSrc(mediaUrl)}
              hidden={!usable}
              muted
              playsInline
              preload="auto"
              onLoadedData={() => setUsable(true)}
              onError={() => setUsable(false)}
            />
          )}
          <OutputFrame
            frame={segment.frame}
            preset={segment.preset}
            source={source}
            limits={limits?.[segment.preset]}
            scale={scale}
            labelled={cropTool}
          />
        </div>
      </div>

      <GlassBar scale={scale} />

      <span className={`ss-card-glass ${styles.chip} ${styles.chipLeft}`}>
        {t("editor.sourceInfo", {
          format: source.format.toUpperCase(),
          duration: num(source.durationSec, 1),
          fps: fpsText(fps),
          alpha: t(source.hasAlpha ? "editor.alpha.yes" : "editor.alpha.no"),
        })}
      </span>
      <span className={`ss-card-glass ${styles.chip} ${styles.chipRight}`}>
        <Icon name="repeat" size={16} />
        {t("editor.preview.quick", {
          loop: t(segment.loop === "pingpong" ? "editor.loop.pingpong" : "editor.loop.normal"),
        })}
      </span>
    </section>
  );
}
