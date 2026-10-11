import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { cx } from "../../../ds/cx";
import { EXIT_FALLBACK_MS, prefersReducedMotion } from "../../../ds/hooks";
import { mediaSrc } from "../../../shared/api/client";
import { useAppInfo } from "../../../shared/api/queries";
import { t } from "../../../shared/strings/ru";
import { frameBox, type Rect } from "../model/crop";
import { useGlide } from "../glide";
import { fpsOf, type Size } from "../model/frame";
import { useEditor, useSegment } from "../model/store";
import { fitScale } from "../model/zoom";
import { signature, useFreshResult, useRender } from "../render/store";
import { CompareResult } from "./CompareResult";
import { CropFrame } from "./CropFrame";
import { ExactVideo } from "./ExactVideo";
import { FramePoster } from "./FramePoster";
import { GlassBar } from "./GlassBar";
import { StageChips } from "./StageChips";
import { StageDrop } from "./StageDrop";
import { usePlayback } from "./usePlayback";
import styles from "./VideoStage.module.css";

/** Room the video leaves around itself for the glass bar on top and the chips at the bottom. */
const INSET = { top: 72, bottom: 64, side: 24 };
/** Between the source and the result in «Сравнить» */
const PAIR_GAP = 24;

/** The canvas on a checkerboard (artboard 3a): the source in a <video>, the output frame over it,
 *  the glass bar and two glass chips. The exact preview (3c, D) plays the draft render over the
 *  quick one; «Сравнить» (3c, H) puts the exported file beside it. */
export function VideoStage({ className }: { className?: string }) {
  const source = useEditor((s) => s.source);
  const mediaUrl = useEditor((s) => s.mediaUrl);
  const projectId = useEditor((s) => s.projectId);
  const zoom = useEditor((s) => s.zoom);
  const cropTool = useEditor((s) => s.cropTool);
  const draft = useEditor((s) => s.frameDraft);
  const segment = useSegment();
  const limits = useAppInfo().data?.limits;
  const exact = useRender((s) => s.exact);
  const previewFile = useRender((s) => s.previewFile);
  const compareOn = useRender((s) => s.compare);
  const result = useFreshResult(segment);

  const viewportRef = useRef<HTMLDivElement>(null);
  const mediaRef = useRef<HTMLDivElement>(null);
  const videoRef = useRef<HTMLVideoElement>(null);
  const shot = useRef<Shot | null>(null);
  const layout = useRef<Layout | null>(null);
  const zoomGlide = useGlide(String(zoom));
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
  // Without segments the quick preview loops the whole video.
  usePlayback(
    videoRef,
    usable,
    segment ?? {
      inFrame: 0,
      outFrame: Math.max(0, (source?.frameCount ?? 1) - 1),
      speed: 1,
      loop: "normal",
    },
    fps,
  );

  const ok = source !== undefined;
  const editingNow = cropTool && segment !== undefined;
  // «Рамка» on or off, and the zoom buttons, move the canvas as a camera (FLIP): the sticker's
  // rectangle starts where it was on screen and settles where it is now. Wheel zoom: at once.
  useLayoutEffect(() => {
    const el = mediaRef.current;
    const viewport = viewportRef.current;
    if (!ok || !el || !viewport || !layout.current) return;
    const now: Shot = { at: anchorOn(el, viewport, layout.current), editing: editingNow, zoom };
    const before = shot.current;
    shot.current = now;
    if (!before || prefersReducedMotion()) return;
    if (before.editing !== now.editing) flip(el, viewport, before.at, now.at, "--duration-slow");
    else if (before.zoom !== now.zoom && zoomGlide) {
      flip(el, viewport, before.at, now.at, "--duration-fast");
    }
  });

  if (!source) return null;

  const present = source.status === "ok" && mediaUrl !== undefined;
  const frame = segment && (draft ?? segment.frame);
  const whole = { x: 0, y: 0, width: source.width, height: source.height };
  // With «Рамка» on, the whole source and the frame over it; otherwise the sticker alone, as
  // it comes out: the crop, or the source squeezed into the stretch box.
  const editing = cropTool && frame !== undefined;
  const view = frame && !editing ? frameBox(frame, source) : whole;
  // The fit takes in a stretch box grown past the source. It follows the committed frame, so a
  // drag never rescales the canvas under the pointer.
  const area = segment && editing ? span(whole, frameBox(segment.frame, source)) : view;
  const comparing = compareOn && result !== undefined && !editing;
  const room = comparing ? { width: (box.width - PAIR_GAP) / 2, height: box.height } : box;
  const fit = fitScale(area, room);
  const scale = zoom === "fit" ? fit : zoom;
  const shown = frame?.mode === "stretch" ? frameBox(frame, source) : whole;
  // Without a playable video, the frame under the playhead stands in for it.
  const poster = !usable && source.status === "ok" ? projectId : undefined;
  const placed = {
    left: (shown.x - view.x) * scale,
    top: (shown.y - view.y) * scale,
    width: shown.width * scale,
    height: shown.height * scale,
  };
  // The draft of the segment as it is now plays over the quick preview; while a newer one is
  // rendered, the quick preview goes on under the progress, dimmed.
  const draftOn = exact && !editing && !comparing && segment !== undefined;
  const ready = draftOn && previewFile?.of === signature(segment);
  const rendering = draftOn && !ready;
  const size = { width: view.width * scale, height: view.height * scale };
  const anchor = segment ? frameBox(segment.frame, source) : whole;
  layout.current = {
    x: (anchor.x - view.x) * scale,
    y: (anchor.y - view.y) * scale,
    width: anchor.width * scale,
    height: anchor.height * scale,
  };

  const media = (
    <div
      ref={mediaRef}
      className={cx(styles.media, !editing && frame && styles.result, rendering && styles.dimmed)}
      style={size}
    >
      {poster && (
        <FramePoster
          projectId={poster}
          total={source.frameCount}
          height={source.height}
          fps={fps}
          className={styles.video}
          style={placed}
        />
      )}
      {!usable && !poster && (
        <span className={styles.placeholder}>
          {t("editor.source", { width: source.width, height: source.height })}
        </span>
      )}
      {present && (
        <video
          ref={videoRef}
          className={styles.video}
          style={placed}
          src={mediaSrc(mediaUrl)}
          hidden={!usable}
          muted
          playsInline
          preload="auto"
          onLoadedData={() => setUsable(true)}
          onError={() => setUsable(false)}
        />
      )}
      {ready && previewFile && <ExactVideo src={previewFile.url} />}
      {segment && frame && editing && (
        <CropFrame
          frame={frame}
          preset={segment.preset}
          source={source}
          limits={limits?.[segment.preset]}
          scale={scale}
        />
      )}
      {comparing && (
        <span className={`ss-card-glass ${styles.paneLabel}`}>{t("editor.compare.source")}</span>
      )}
    </div>
  );

  return (
    <section className={`${styles.stage} ${className ?? ""}`} aria-label={t("editor.canvas")}>
      <div
        ref={viewportRef}
        className={styles.viewport}
        style={{ padding: `${INSET.top}px ${INSET.side}px ${INSET.bottom}px` }}
      >
        {comparing && segment && result ? (
          <div className={styles.pair} style={{ gap: PAIR_GAP }}>
            {media}
            <CompareResult result={result} segment={segment} fps={fps} size={size} />
          </div>
        ) : (
          media
        )}
      </div>

      <GlassBar scale={scale} comparing={comparing} />
      <StageChips source={source} fps={fps} draft={draftOn} ready={ready} />
      <StageDrop />
    </section>
  );
}

/** The size of the smallest box holding both rects. */
function span(a: Rect, b: Rect): Size {
  return {
    width: Math.max(a.x + a.width, b.x + b.width) - Math.min(a.x, b.x),
    height: Math.max(a.y + a.height, b.y + b.height) - Math.min(a.y, b.y),
  };
}

/** Where the sticker's rectangle is drawn inside the media box, in display px. */
type Layout = Rect;

/** The sticker's rectangle in viewport coordinates, with the state that put it there. */
interface Shot {
  at: Rect;
  editing: boolean;
  zoom: unknown;
}

/** The sticker's rectangle on screen, in the viewport's frame. Offsets ignore a running
 *  transform, so a FLIP started mid-FLIP starts from the true layout. */
function anchorOn(el: HTMLElement, viewport: HTMLElement, layout: Layout): Rect {
  return {
    x: el.offsetLeft - viewport.scrollLeft + layout.x,
    y: el.offsetTop - viewport.scrollTop + layout.y,
    width: layout.width,
    height: layout.height,
  };
}

/** Puts the media box back so that its sticker rectangle covers `from`, then lets it settle
 *  at `to` over `duration` (a token name). Overflow is held while it runs, so no scrollbar
 *  flashes under the grown canvas. */
function flip(el: HTMLElement, viewport: HTMLElement, from: Rect, to: Rect, duration: string) {
  if (!(to.width > 0) || !(from.width > 0)) return;
  const s = from.width / to.width;
  const origin = { x: el.offsetLeft - viewport.scrollLeft, y: el.offsetTop - viewport.scrollTop };
  const dx = from.x - origin.x - s * (to.x - origin.x);
  const dy = from.y - origin.y - s * (to.y - origin.y);
  el.style.transition = "none";
  el.style.transformOrigin = "0 0";
  el.style.transform = `translate(${dx}px, ${dy}px) scale(${s})`;
  viewport.style.overflow = "hidden";
  void el.offsetWidth;
  el.style.transition = `transform var(${duration}) var(--ease-soft)`;
  el.style.transform = "";
  // Ends on its own transitionend, or after the fallback when no frame ran (a hidden window).
  const done = (e?: TransitionEvent) => {
    if (e && (e.target !== el || e.propertyName !== "transform")) return;
    el.removeEventListener("transitionend", done);
    window.clearTimeout(timer);
    el.style.transition = "";
    el.style.transformOrigin = "";
    viewport.style.overflow = "";
  };
  const timer = window.setTimeout(done, EXIT_FALLBACK_MS);
  el.addEventListener("transitionend", done);
}
